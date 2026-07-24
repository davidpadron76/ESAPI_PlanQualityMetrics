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

            Structure body = ss.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL" && !s.IsEmpty)
                             ?? ss.Structures.FirstOrDefault(s => s.Id.ToUpper() == "BODY" && !s.IsEmpty);

            if (body == null)
            {
                MessageBox.Show("No se encontró la estructura BODY (External).");
                return;
            }

            if (plan.Dose == null)
            {
                MessageBox.Show("El plan no tiene una dosis calculada. Calcula el plan antes de generar el reporte.");
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
            // Volúmenes/dosis fuera de rango del DVH pueden devolver NaN: se reportan como "N/D" en vez de un número engañoso.

            // 5. CÁLCULO DE ÍNDICES COMPLEJOS
            // CI RTOG (PITV): Volumen cubierto por la isodosis de prescripción / Volumen del target.
            double ciRTOG = (volPtv > 0) ? (volBody100 / volPtv) : 0;
            // CI Paddick: (TV_PIV)^2 / (TV * PIV) — penaliza tanto el derrame como la subcobertura (Paddick, 2000).
            double ciPaddick = (volPtv * volBody100 > 0) ? ((volPtv100 * volPtv100) / (volPtv * volBody100)) : 0;
            // Gradient Index (Paddick): relación entre el volumen de la isodosis al 50% y al 100% de Rx.
            double gradientIdx = (volBody100 > 0) ? (volBody50 / volBody100) : 0;
            // Homogeneity Index (ICRU 83): (D2%-D98%)/D50%; ideal cercano a 0.
            double homoIdx = (d50 > 0) ? (d2 - d98) / d50 : 0;

            // MUR (Monitor Unit Ratio): UM totales entregadas por cGy de dosis por fracción.
            // Nota: NO equivale al "Modulation Factor" clásico de complejidad de segmentos/MLC;
            // es un indicador indirecto de cuánta modulación exige el plan para entregar la dosis.
            double totalMU = 0;
            foreach (var beam in plan.Beams.Where(b => !b.IsSetupField))
            {
                totalMU += beam.Meterset.Value;
            }

            double dosePerFx = plan.DosePerFraction.Dose;
            if (plan.DosePerFraction.Unit == DoseValue.DoseUnit.Gy) dosePerFx *= 100.0;
            double mur = (dosePerFx > 0) ? totalMU / dosePerFx : 0;

            // 6. PREPARAR LISTA DE RESULTADOS (agrupados por categoría para la interfaz)
            const string catPrescripcion = "Prescripción";
            const string catVolumenes = "Volúmenes y Cobertura";
            const string catDosisPtv = "Dosis en PTV";
            const string catIndices = "Índices de Calidad del Plan";
            const string catComplejidad = "Complejidad de Entrega";

            var resultados = new List<MetricaQA>
            {
                new MetricaQA { Categoria = catPrescripcion, Nombre = "Dosis Prescrita (Rx)", Valor = $"{doseRx_cGy:F0} cGy", Referencia = "Planificación" },

                new MetricaQA { Categoria = catVolumenes, Nombre = "Volumen PTV", Valor = $"{volPtv:F2} cc", Referencia = "Estructura Target" },
                new MetricaQA { Categoria = catVolumenes, Nombre = "Volumen V100% (Cuerpo)", Valor = FormatValor(volBody100, "F2", "cc"), Referencia = "Volumen del cuerpo cubierto por Rx" },
                new MetricaQA { Categoria = catVolumenes, Nombre = "Volumen V50% (Cuerpo)", Valor = FormatValor(volBody50, "F2", "cc"), Referencia = "Derrame de dosis baja (50% Rx)" },
                new MetricaQA { Categoria = catVolumenes, Nombre = "Cobertura del PTV (V100%)", Valor = $"{(volPtv > 0 ? (volPtv100 / volPtv * 100) : 0):F2} %", Referencia = "% del volumen PTV que recibe Rx" },

                new MetricaQA { Categoria = catDosisPtv, Nombre = "Dosis Máxima (Global)", Valor = FormatValor(dmax, "F1", "cGy"), Referencia = $"{(doseRx_cGy > 0 && !double.IsNaN(dmax) ? (dmax / doseRx_cGy * 100) : 0):F1}% de Rx" },
                new MetricaQA { Categoria = catDosisPtv, Nombre = "D2% (dosis casi máxima)", Valor = FormatValor(d2, "F1", "cGy"), Referencia = "Cercana al máximo (ICRU 83)" },
                new MetricaQA { Categoria = catDosisPtv, Nombre = "D50% (dosis mediana)", Valor = FormatValor(d50, "F1", "cGy"), Referencia = "Referencia de homogeneidad" },
                new MetricaQA { Categoria = catDosisPtv, Nombre = "D98% (dosis casi mínima)", Valor = FormatValor(d98, "F1", "cGy"), Referencia = "Cercana al mínimo (ICRU 83)" },

                new MetricaQA { Categoria = catIndices, Nombre = "Índice de Conformidad de Paddick (CI)", Valor = $"{ciPaddick:F3}", Referencia = "Ideal: 1.0 (Paddick, 2000)" },
                new MetricaQA { Categoria = catIndices, Nombre = "Índice de Conformidad RTOG (PITV)", Valor = $"{ciRTOG:F3}", Referencia = "Ideal: 1.0" },
                new MetricaQA { Categoria = catIndices, Nombre = "Índice de Gradiente de Dosis (GI)", Valor = $"{gradientIdx:F2}", Referencia = "V50% / V100% — menor es más conformado" },
                new MetricaQA { Categoria = catIndices, Nombre = "Índice de Homogeneidad (HI, ICRU 83)", Valor = $"{homoIdx:F3}", Referencia = "(D2%-D98%)/D50% — ideal cercano a 0" },

                new MetricaQA { Categoria = catComplejidad, Nombre = "MUR (Monitor Unit Ratio)", Valor = $"{mur:F1} UM/cGy", Referencia = "UM totales / dosis por fracción" }
            };

            // 7. LANZAR INTERFAZ
            var reportView = new ReporteView();
            reportView.SetData(context.Patient.LastName + ", " + context.Patient.FirstName, plan.Id, resultados);

            window.Content = reportView;
            window.Title = $"QA Report - {context.Patient.Id}";
            window.Width = 680;
            window.Height = 720;
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
            if (dv.Unit == DoseValue.DoseUnit.Unknown) return double.NaN; // Valor no determinable en el DVH
            return dv.Dose; // Fallback
        }

        // Helper: formatea un valor numérico como "N/D" si es NaN (fuera de rango del DVH),
        // en vez de mostrar un número engañoso.
        private static string FormatValor(double value, string format, string unidad)
        {
            return double.IsNaN(value) ? "N/D" : $"{value.ToString(format)} {unidad}";
        }
    }
}