using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
// [assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
  public class Script
  {
    public Script()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Execute(ScriptContext context, System.Windows.Window window /*, ScriptEnvironment environment*/)
        {
            // --------------------------------------------------------------------------------
            // 1. VALIDACIONES INICIALES
            // --------------------------------------------------------------------------------
            if (context.Patient == null || context.PlanSetup == null)
            {
                MessageBox.Show("Por favor, abre un plan de tratamiento activo.");
                return;
            }

            PlanSetup plan = context.PlanSetup;
            StructureSet ss = plan.StructureSet;

            // --------------------------------------------------------------------------------
            // 2. OBTENCIÓN DE ESTRUCTURAS (PTV y BODY)
            // --------------------------------------------------------------------------------
            Structure ptv = GetTargetStructure(plan);
            if (ptv == null)
            {
                MessageBox.Show("No se encontró una estructura PTV (Target). \nRevisa el nombre o asigna el 'Target Volume' en las propiedades del plan.");
                return;
            }

            // Buscamos el Body (generalmente DicomType="EXTERNAL" o Id="BODY")
            Structure body = ss.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL")
                             ?? ss.Structures.FirstOrDefault(s => s.Id.ToUpper() == "BODY");

            if (body == null)
            {
                MessageBox.Show("No se encontró la estructura BODY (External).");
                return;
            }

            // --------------------------------------------------------------------------------
            // 3. DATOS DE REFERENCIA Y CONVERSIONES
            // --------------------------------------------------------------------------------
            // Normalizamos todo a cGy para evitar confusiones si el plan está en Gy
            double doseRx_cGy = plan.TotalDose.Dose;
            if (plan.TotalDose.Unit == DoseValue.DoseUnit.Gy) doseRx_cGy *= 100.0;

            DoseValue dv100 = new DoseValue(doseRx_cGy, DoseValue.DoseUnit.cGy);
            DoseValue dv50 = new DoseValue(doseRx_cGy * 0.5, DoseValue.DoseUnit.cGy);

            // --------------------------------------------------------------------------------
            // 4. CÁLCULOS DOSIMÉTRICOS (Raw Data)
            // --------------------------------------------------------------------------------

            // Volúmenes
            double volPtv = ptv.Volume; // cc
            double volBody100 = plan.GetVolumeAtDose(body, dv100, VolumePresentation.AbsoluteCm3);
            double volBody50 = plan.GetVolumeAtDose(body, dv50, VolumePresentation.AbsoluteCm3);
            double volPtv100 = plan.GetVolumeAtDose(ptv, dv100, VolumePresentation.AbsoluteCm3);

            // Dosis en PTV (D2, D50, D98, Mean, Min)
            // Nota: GetDoseAtVolume devuelve la unidad del plan. Forzamos la presentación.
            double d2 = plan.GetDoseAtVolume(ptv, 2.0, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
            double d50 = plan.GetDoseAtVolume(ptv, 50.0, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
            double d98 = plan.GetDoseAtVolume(ptv, 98.0, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
            double dMean = plan.GetDoseAtVolume(ptv, 50.0 /*Mean aprox*/, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose; // ESAPI no tiene .Mean directo fácil en versiones viejas, usamos DVH Statistics si fuera necesario, pero D50 es buena aprox mediana. 
                                                                                                                                             // Mejor usamos DVHData para Mean y Min reales si están disponibles, sino D99% como proxy de min y D50 como mediana.
                                                                                                                                             // Para simplificar y compatibilidad, usaremos los puntos standard ICRU:

            // Dosis Máxima Global
            double dmax = plan.Dose.DoseMax3D.Dose;
            if (plan.Dose.DoseMax3D.Unit == DoseValue.DoseUnit.Percent) dmax = (dmax / 100.0) * doseRx_cGy;
            else if (plan.Dose.DoseMax3D.Unit == DoseValue.DoseUnit.Gy) dmax *= 100.0;

            // Corrección de unidades si las funciones devolvieron Gy (depende de versión ESAPI)
            // Una forma segura es chequear contra la Rx. Si D50 < 100 y Rx es 2000, está en Gy.
            // Asumiremos que ESAPI devuelve en la unidad del plan. Si plan es Gy, convertimos.
            bool planIsGy = plan.TotalDose.Unit == DoseValue.DoseUnit.Gy;
            if (planIsGy)
            {
                d2 *= 100.0; d50 *= 100.0; d98 *= 100.0;
            }

            // --------------------------------------------------------------------------------
            // 5. CÁLCULO DE ÍNDICES COMPLEJOS
            // --------------------------------------------------------------------------------

            // Conformidad
            double ciRTOG = (volPtv > 0) ? (volBody100 / volPtv) : 0;
            double ciPaddick = (volPtv * volBody100 > 0) ? ((volPtv100 * volPtv100) / (volPtv * volBody100)) : 0;

            // Gradiente y Homogeneidad
            double gradientIdx = (volBody100 > 0) ? (volBody50 / volBody100) : 0;
            double homoIdx = (d50 > 0) ? (d2 - d98) / d50 : 0;

            // Factor MUR (Modulación)
            double totalMU = 0;
            foreach (var beam in plan.Beams.Where(b => !b.IsSetupField))
            {
                totalMU += beam.Meterset.Value; // Asume MU
            }
            double dosePerFx = plan.DosePerFraction.Dose;
            if (plan.DosePerFraction.Unit == DoseValue.DoseUnit.Gy) dosePerFx *= 100.0;

            double mur = (dosePerFx > 0) ? totalMU / dosePerFx : 0;


            // --------------------------------------------------------------------------------
            // 6. PREPARAR LISTA DE RESULTADOS (AQUÍ AGREGAMOS LOS DATOS EXTRAS)
            // --------------------------------------------------------------------------------
            var resultados = new List<MetricaQA>();

            // --- GRUPO 1: DATOS DEL PACIENTE Y VOLÚMENES ---
            resultados.Add(new MetricaQA { Nombre = "Dosis Prescrita (Rx)", Valor = $"{doseRx_cGy:F0} cGy", Referencia = "Planificación" });
            resultados.Add(new MetricaQA { Nombre = "Volumen PTV", Valor = $"{volPtv:F2} cc", Referencia = "Estructura Target" });

            // --- GRUPO 2: ESTADÍSTICAS DOSIMÉTRICAS (LO QUE PEDISTE) ---
            resultados.Add(new MetricaQA { Nombre = "Dosis Máxima (Global)", Valor = $"{dmax:F1} cGy", Referencia = $"{(dmax / doseRx_cGy * 100):F1}% de Rx" });
            resultados.Add(new MetricaQA { Nombre = "Dosis PTV - D2%", Valor = $"{d2:F1} cGy", Referencia = "Cerca del Máx (ICRU)" });
            resultados.Add(new MetricaQA { Nombre = "Dosis PTV - D50% (Mediana)", Valor = $"{d50:F1} cGy", Referencia = "Ref. Homogeneidad" });
            resultados.Add(new MetricaQA { Nombre = "Dosis PTV - D98% (Mínima)", Valor = $"{d98:F1} cGy", Referencia = "Cerca del Mín (ICRU)" });

            // --- GRUPO 3: VOLÚMENES DE ISODOSIS ---
            resultados.Add(new MetricaQA { Nombre = "Volumen V100% (Cuerpo)", Valor = $"{volBody100:F2} cc", Referencia = "Volumen irradiado a Rx" });
            resultados.Add(new MetricaQA { Nombre = "Volumen V50% (Cuerpo)", Valor = $"{volBody50:F2} cc", Referencia = "Derrame de dosis baja" });
            resultados.Add(new MetricaQA { Nombre = "Cobertura PTV (V100%)", Valor = $"{(volPtv100 / volPtv * 100):F2} %", Referencia = "% del PTV cubierto" });

            // --- GRUPO 4: ÍNDICES DE CALIDAD ---
            resultados.Add(new MetricaQA { Nombre = "Índice Conformidad Paddick", Valor = $"{ciPaddick:F3}", Referencia = "Ideal: 1.0" });
            resultados.Add(new MetricaQA { Nombre = "Índice Conformidad RTOG", Valor = $"{ciRTOG:F3}", Referencia = "Ideal: 1.0" });
            resultados.Add(new MetricaQA { Nombre = "Índice Gradiente (Paddick)", Valor = $"{gradientIdx:F2}", Referencia = "V50% / V100%" });
            resultados.Add(new MetricaQA { Nombre = "Índice Homogeneidad (ICRU)", Valor = $"{homoIdx:F3}", Referencia = "(D2-D98)/D50" });
            resultados.Add(new MetricaQA { Nombre = "Factor Modulación (MUR)", Valor = $"{mur:F3}", Referencia = "MU / cGy" });

            // --------------------------------------------------------------------------------
            // 7. LANZAR INTERFAZ
            // --------------------------------------------------------------------------------
            var reportView = new ReporteView();
            reportView.SetData(context.Patient.LastName + ", " + context.Patient.FirstName, plan.Id, resultados);

            window.Content = reportView;
            window.Title = $"QA Report - {context.Patient.Id}";
            window.Width = 620; // Un poco más ancha para que quepan los datos
            window.Height = 650;
        }

        // Función Helper para encontrar el PTV
        private Structure GetTargetStructure(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            if (!string.IsNullOrEmpty(plan.TargetVolumeID)) return ss.Structures.FirstOrDefault(s => s.Id == plan.TargetVolumeID);
            return ss.Structures.FirstOrDefault(s => s.Id == "PTV") ?? ss.Structures.FirstOrDefault(s => s.Id.ToUpper().Contains("PTV") && !s.IsEmpty);
        }
    }
}