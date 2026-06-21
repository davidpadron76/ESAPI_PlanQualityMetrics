using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

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
            // 1. VALIDACIONES INICIALES
            if (context.Patient == null || context.PlanSetup == null)
            {
                MessageBox.Show("Por favor, abre un plan de tratamiento activo.");
                return;
            }

            PlanSetup plan = context.PlanSetup;
            StructureSet ss = plan.StructureSet;

            // 2. OBTENCIÓN DE ESTRUCTURAS (PTV y BODY)
            Structure ptv = GetTargetStructure(plan);
            if (ptv == null)
            {
                MessageBox.Show("No se encontró una estructura PTV (Target). \nRevisa el nombre o asigna el 'Target Volume' en las propiedades del plan.");
                return;
            }

            Structure body = ss.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL")
                             ?? ss.Structures.FirstOrDefault(s => s.Id.ToUpper() == "BODY");

            if (body == null)
            {
                MessageBox.Show("No se encontró la estructura BODY (External).");
                return;
            }

            // 3. DATOS DE REFERENCIA Y CONVERSIONES
            double doseRx_cGy = plan.TotalDose.Dose;
            if (plan.TotalDose.Unit == DoseValue.DoseUnit.Gy) doseRx_cGy *= 100.0;

            DoseValue dv100 = new DoseValue(doseRx_cGy, DoseValue.DoseUnit.cGy);
            DoseValue dv50 = new DoseValue(doseRx_cGy * 0.5, DoseValue.DoseUnit.cGy);

            // 4. CÁLCULOS DOSIMÉTRICOS (Raw Data)
            double volPtv = ptv.Volume; // cc
            double volBody100 = plan.GetVolumeAtDose(body, dv100, VolumePresentation.AbsoluteCm3);
            double volBody50 = plan.GetVolumeAtDose(body, dv50, VolumePresentation.AbsoluteCm3);
            double volPtv100 = plan.GetVolumeAtDose(ptv, dv100, VolumePresentation.AbsoluteCm3);

            // Obtener DoseValues y convertirlos de forma segura a cGy
            DoseValue dvD2 = plan.GetDoseAtVolume(ptv, 2.0, VolumePresentation.Relative, DoseValuePresentation.Absolute);
            DoseValue dvD50 = plan.GetDoseAtVolume(ptv, 50.0, VolumePresentation.Relative, DoseValuePresentation.Absolute);
            DoseValue dvD98 = plan.GetDoseAtVolume(ptv, 98.0, VolumePresentation.Relative, DoseValuePresentation.Absolute);
            DoseValue dvMax = plan.Dose.DoseMax3D;

            double d2 = ConvertToCgy(dvD2, doseRx_cGy);
            double d50 = ConvertToCgy(dvD50, doseRx_cGy);
            double d98 = ConvertToCgy(dvD98, doseRx_cGy);
            double dmax = ConvertToCgy(dvMax, doseRx_cGy);

            // 5. CÁLCULO DE ÍNDICES COMPLEJOS
            double ciRTOG = (volPtv > 0) ? (volBody100 / volPtv) : 0;
            double ciPaddick = (volPtv * volBody100 > 0) ? ((volPtv100 * volPtv100) / (volPtv * volBody100)) : 0;
            double gradientIdx = (volBody100 > 0) ? (volBody50 / volBody100) : 0;
            double homoIdx = (d50 > 0) ? (d2 - d98) / d50 : 0;

            // Cálculo del Factor de Modulación (MF)
            double totalMU = 0;
            foreach (var beam in plan.Beams.Where(b => !b.IsSetupField))
            {
                totalMU += beam.Meterset.Value;
            }

            double dosePerFx = plan.DosePerFraction.Dose;
            if (plan.DosePerFraction.Unit == DoseValue.DoseUnit.Gy) dosePerFx *= 100.0;
            double mf = (dosePerFx > 0) ? totalMU / dosePerFx : 0;

            // 6. PREPARAR LISTA DE RESULTADOS
            var resultados = new List<MetricaQA>
            {
                new MetricaQA { Nombre = "Dosis Prescrita (Rx)", Valor = $"{doseRx_cGy:F0} cGy", Referencia = "Planificación" },
                new MetricaQA { Nombre = "Volumen PTV", Valor = $"{volPtv:F2} cc", Referencia = "Estructura Target" },
                new MetricaQA { Nombre = "Dosis Máxima (Global)", Valor = $"{dmax:F1} cGy", Referencia = $"{(doseRx_cGy > 0 ? (dmax / doseRx_cGy * 100) : 0):F1}% de Rx" },
                new MetricaQA { Nombre = "Dosis PTV - D2%", Valor = $"{d2:F1} cGy", Referencia = "Cerca del Máx (ICRU)" },
                new MetricaQA { Nombre = "Dosis PTV - D50% (Mediana)", Valor = $"{d50:F1} cGy", Referencia = "Ref. Homogeneidad" },
                new MetricaQA { Nombre = "Dosis PTV - D98% (Mínima)", Valor = $"{d98:F1} cGy", Referencia = "Cerca del Mín (ICRU)" },
                new MetricaQA { Nombre = "Volumen V100% (Cuerpo)", Valor = $"{volBody100:F2} cc", Referencia = "Volumen irradiado a Rx" },
                new MetricaQA { Nombre = "Volumen V50% (Cuerpo)", Valor = $"{volBody50:F2} cc", Referencia = "Derrame de dosis baja" },
                new MetricaQA { Nombre = "Cobertura PTV (V100%)", Valor = $"{(volPtv > 0 ? (volPtv100 / volPtv * 100) : 0):F2} %", Referencia = "% del PTV cubierto" },
                new MetricaQA { Nombre = "Índice Conformidad Paddick", Valor = $"{ciPaddick:F3}", Referencia = "Ideal: 1.0" },
                new MetricaQA { Nombre = "Índice Conformidad RTOG", Valor = $"{ciRTOG:F3}", Referencia = "Ideal: 1.0" },
                new MetricaQA { Nombre = "Índice Gradiente (Paddick)", Valor = $"{gradientIdx:F2}", Referencia = "V50% / V100%" },
                new MetricaQA { Nombre = "Índice Homogeneidad (ICRU)", Valor = $"{homoIdx:F3}", Referencia = "(D2-D98)/D50" },
                new MetricaQA { Nombre = "Factor Modulación (MF)", Valor = $"{mf:F3}", Referencia = "MU / cGy" }
            };

            // 7. LANZAR INTERFAZ
            var reportView = new ReporteView();
            reportView.SetData(context.Patient.LastName + ", " + context.Patient.FirstName, plan.Id, resultados);

            window.Content = reportView;
            window.Title = $"QA Report - {context.Patient.Id}";
            window.Width = 620;
            window.Height = 650;
        }

        // Helper para encontrar el PTV
        private Structure GetTargetStructure(PlanSetup plan)
        {
            var ss = plan.StructureSet;
            if (!string.IsNullOrEmpty(plan.TargetVolumeID)) return ss.Structures.FirstOrDefault(s => s.Id == plan.TargetVolumeID);
            return ss.Structures.FirstOrDefault(s => s.Id == "PTV") ?? ss.Structures.FirstOrDefault(s => s.Id.ToUpper().Contains("PTV") && !s.IsEmpty);
        }

        // Helper ESAPI: Conversión segura de unidades de dosis a cGy
        private static double ConvertToCgy(DoseValue dv, double rxInCgy)
        {
            if (dv.Unit == DoseValue.DoseUnit.Gy) return dv.Dose * 100.0;
            if (dv.Unit == DoseValue.DoseUnit.cGy) return dv.Dose;
            if (dv.Unit == DoseValue.DoseUnit.Percent) return (dv.Dose / 100.0) * rxInCgy;
            return dv.Dose; // Fallback
        }
    }
}