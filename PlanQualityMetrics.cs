using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: ESAPIScript(IsWriteable = true)]
[assembly: AssemblyVersion("2.0.0.1")]
[assembly: AssemblyFileVersion("2.0.0.1")]
[assembly: AssemblyInformationalVersion("2.0")]

namespace VMS.TPS
{
    public class Script
    {
        // IDs de las estructuras auxiliares temporales (anillos locales). Se crean una vez y se
        // reutilizan para cada PTV; siempre se eliminan al finalizar (bloque finally), incluso si
        // ocurre un error a mitad de proceso.
        private const string IdRingV100A = "zPTVring_v100_A";
        private const string IdRingV100B = "zPTVring_v100_B";
        private const string IdRingV50A = "zPTVring_v50_A";
        private const string IdRingV50B = "zPTVring_v50_B";
        private const string IdDummy = "zDummyLocal";

        // Márgenes (mm) de los anillos locales alrededor de cada PTV, usados para aislar el volumen
        // de isodosis de ESA lesión de las lesiones vecinas. Se usan dos márgenes por zona (V100%/V50%):
        // si ambos no coinciden en volumen calculado, se asume "dose bridging" (solape de isodosis entre
        // lesiones cercanas) y el índice correspondiente se reporta como N/D en vez de un número engañoso.
        // Valores tomados como referencia de Kiragroh (ESAPI_SRS-MultiMets-localMetrics); ajustar según
        // el tamaño típico de lesiones y la separación entre blancos de cada centro.
        private const double MargenV100A_mm = 4.0;
        private const double MargenV100B_mm = 5.0;
        private const double MargenV50A_mm = 7.7;
        private const double MargenV50B_mm = 8.7;

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

            var ptvs = ss.Structures
                .Where(s => !s.IsEmpty && s.DicomType.ToUpper() == "PTV")
                .OrderBy(s => s.Id)
                .ToList();

            if (ptvs.Count == 0)
            {
                MessageBox.Show("No se encontraron estructuras PTV en el set de estructuras.");
                return;
            }

            if (plan.Dose == null)
            {
                MessageBox.Show("El plan no tiene una dosis calculada. Calcula el plan antes de generar el reporte.");
                return;
            }

            // 2. DATOS DE REFERENCIA DEL PLAN
            double doseRx_cGy = plan.TotalDose.Dose;
            if (plan.TotalDose.Unit == DoseValue.DoseUnit.Gy) doseRx_cGy *= 100.0;

            bool esSRSFraccionUnica = plan.NumberOfFractions.HasValue && plan.NumberOfFractions.Value == 1;

            double totalMU = 0;
            foreach (var beam in plan.Beams.Where(b => !b.IsSetupField))
            {
                totalMU += beam.Meterset.Value;
            }
            double dosePerFx = plan.DosePerFraction.Dose;
            if (plan.DosePerFraction.Unit == DoseValue.DoseUnit.Gy) dosePerFx *= 100.0;
            double mur = (dosePerFx > 0) ? totalMU / dosePerFx : 0;

            // Isocentros de los campos de tratamiento (para distancia lesión-isocentro en SRS multi-lesión).
            var isocentros = plan.Beams.Where(b => !b.IsSetupField).Select(b => b.IsocenterPosition).ToList();

            const string catPlan = "Plan";
            var resultados = new List<MetricaQA>
            {
                new MetricaQA { Categoria = catPlan, Nombre = "Dosis Prescrita (Rx)", Valor = $"{doseRx_cGy:F0} cGy", Referencia = "Planificación" },
                new MetricaQA { Categoria = catPlan, Nombre = "Número de Fracciones", Valor = plan.NumberOfFractions.HasValue ? plan.NumberOfFractions.Value.ToString() : "N/D", Referencia = esSRSFraccionUnica ? "SRS de fracción única" : "SRS/SBRT fraccionado" },
                new MetricaQA { Categoria = catPlan, Nombre = "MUR (Monitor Unit Ratio)", Valor = $"{mur:F1} UM/cGy", Referencia = "UM totales / dosis por fracción" }
            };

            // 3. ESTRUCTURAS AUXILIARES (ANILLOS LOCALES) — requieren permiso de escritura en el set de estructuras.
            context.Patient.BeginModifications();

            Structure ringV100A = null, ringV100B = null, ringV50A = null, ringV50B = null, dummy = null;

            try
            {
                RemoveIfExists(ss, IdRingV100A);
                RemoveIfExists(ss, IdRingV100B);
                RemoveIfExists(ss, IdRingV50A);
                RemoveIfExists(ss, IdRingV50B);
                RemoveIfExists(ss, IdDummy);

                ringV100A = ss.AddStructure("CONTROL", IdRingV100A);
                ringV100A.ConvertToHighResolution();
                ringV100B = ss.AddStructure("CONTROL", IdRingV100B);
                ringV100B.ConvertToHighResolution();
                ringV50A = ss.AddStructure("CONTROL", IdRingV50A);
                ringV50A.ConvertToHighResolution();
                ringV50B = ss.AddStructure("CONTROL", IdRingV50B);
                ringV50B.ConvertToHighResolution();
                dummy = ss.AddStructure("CONTROL", IdDummy);

                // 4. CÁLCULO DE ÍNDICES LOCALES POR CADA PTV
                foreach (Structure ptv in ptvs)
                {
                    string cat = $"PTV: {ptv.Id}";

                    double localRxCgy = GetLocalRxCgy(plan, ptv, doseRx_cGy);
                    DoseValue dvLocalRx100 = new DoseValue(localRxCgy, DoseValue.DoseUnit.cGy);
                    DoseValue dvLocalRx50 = new DoseValue(localRxCgy * 0.5, DoseValue.DoseUnit.cGy);

                    double d2Local = ConvertToCgy(plan.GetDoseAtVolume(ptv, 2.0, VolumePresentation.Relative, DoseValuePresentation.Absolute), localRxCgy);
                    double d50Local = ConvertToCgy(plan.GetDoseAtVolume(ptv, 50.0, VolumePresentation.Relative, DoseValuePresentation.Absolute), localRxCgy);
                    double d98Local = ConvertToCgy(plan.GetDoseAtVolume(ptv, 98.0, VolumePresentation.Relative, DoseValuePresentation.Absolute), localRxCgy);

                    // Se excluyen PTVs cuya mediana de dosis no alcanza su propia Rx (p.ej. estructuras
                    // auxiliares o lesiones aún no tratadas a ese nivel de dosis en planes SIB).
                    if (double.IsNaN(d50Local) || d50Local < localRxCgy)
                    {
                        resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Estado", Valor = "Excluido del análisis", Referencia = "D50% menor que su dosis de prescripción" });
                        continue;
                    }

                    // Expandir el PTV (o una copia en alta resolución si el PTV no lo está) para generar los anillos locales.
                    if (ptv.IsHighResolution)
                    {
                        ringV100A.SegmentVolume = ptv.Margin(MargenV100A_mm);
                        ringV100B.SegmentVolume = ptv.Margin(MargenV100B_mm);
                        ringV100B.SegmentVolume = ringV100A.Or(ringV100B);
                        ringV50A.SegmentVolume = ptv.Margin(MargenV50A_mm);
                        ringV50B.SegmentVolume = ptv.Margin(MargenV50B_mm);
                        ringV50B.SegmentVolume = ringV50A.Or(ringV50B);
                    }
                    else
                    {
                        dummy.SegmentVolume = ptv.SegmentVolume;
                        if (dummy.CanConvertToHighResolution()) dummy.ConvertToHighResolution();

                        ringV100A.SegmentVolume = dummy.Margin(MargenV100A_mm);
                        ringV100B.SegmentVolume = dummy.Margin(MargenV100B_mm);
                        ringV100B.SegmentVolume = ringV100A.Or(ringV100B);
                        ringV50A.SegmentVolume = dummy.Margin(MargenV50A_mm);
                        ringV50B.SegmentVolume = dummy.Margin(MargenV50B_mm);
                        ringV50B.SegmentVolume = ringV50A.Or(ringV50B);
                    }

                    double v100A = plan.GetVolumeAtDose(ringV100A, dvLocalRx100, VolumePresentation.AbsoluteCm3);
                    double v100B = plan.GetVolumeAtDose(ringV100B, dvLocalRx100, VolumePresentation.AbsoluteCm3);
                    double v50A = plan.GetVolumeAtDose(ringV50A, dvLocalRx50, VolumePresentation.AbsoluteCm3);
                    double v50B = plan.GetVolumeAtDose(ringV50B, dvLocalRx50, VolumePresentation.AbsoluteCm3);

                    bool bridgingV100 = VolumesDiffer(v100A, v100B);
                    bool bridgingV50 = VolumesDiffer(v50A, v50B);

                    double ptv100 = plan.GetVolumeAtDose(ptv, dvLocalRx100, VolumePresentation.AbsoluteCm3);
                    double coberturaPct = (ptv.Volume > 0) ? (ptv100 / ptv.Volume * 100.0) : 0;

                    double ciLocal = (!bridgingV100 && ptv.Volume > 0 && v100A > 0) ? (ptv100 * ptv100) / (ptv.Volume * v100A) : double.NaN;
                    double rtogLocal = (!bridgingV100 && ptv.Volume > 0) ? v100A / ptv.Volume : double.NaN;
                    double giLocal = (!bridgingV50 && v100A > 0) ? v50A / v100A : double.NaN;
                    double hiLocal = (d50Local > 0) ? (d2Local - d98Local) / d50Local : double.NaN;

                    double distCm = double.NaN;
                    if (isocentros.Count > 0)
                    {
                        distCm = isocentros.Min(iso => (ptv.CenterPoint - iso).Length / 10.0);
                    }

                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Volumen", Valor = $"{ptv.Volume:F2} cc", Referencia = "Volumen de la estructura" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Dosis de Prescripción (local)", Valor = $"{localRxCgy:F0} cGy", Referencia = (localRxCgy == doseRx_cGy) ? "= Rx del plan" : "Rx propia (Reference Point)" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Cobertura (V100% local)", Valor = $"{coberturaPct:F2} %", Referencia = "% del PTV que recibe su Rx local" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "D2% (dosis casi máxima)", Valor = FormatValor(d2Local, "F1", "cGy"), Referencia = "Cercana al máximo (ICRU 83)" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "D50% (dosis mediana)", Valor = FormatValor(d50Local, "F1", "cGy"), Referencia = "Referencia de homogeneidad" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "D98% (dosis casi mínima)", Valor = FormatValor(d98Local, "F1", "cGy"), Referencia = "Cercana al mínimo (ICRU 83)" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Índice de Conformidad de Paddick (local)", Valor = bridgingV100 ? "N/D" : $"{ciLocal:F3}", Referencia = bridgingV100 ? "Posible dose bridging con otra lesión" : "Ideal: 1.0 (Paddick, 2000)" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Índice de Conformidad RTOG/PITV (local)", Valor = bridgingV100 ? "N/D" : $"{rtogLocal:F3}", Referencia = bridgingV100 ? "Posible dose bridging con otra lesión" : "Ideal: 1.0" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Índice de Gradiente de Dosis (local, GI)", Valor = bridgingV50 ? "N/D" : $"{giLocal:F2}", Referencia = bridgingV50 ? "Posible dose bridging con otra lesión" : "V50%/V100% local — menor es más conformado" });
                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Índice de Homogeneidad (HI, ICRU 83)", Valor = FormatValor(hiLocal, "F3", null), Referencia = "(D2%-D98%)/D50% — ideal cercano a 0" });

                    if (esSRSFraccionUnica)
                    {
                        DoseValue dv12Gy = new DoseValue(1200, DoseValue.DoseUnit.cGy);
                        double v12Ring = plan.GetVolumeAtDose(ringV50A, dv12Gy, VolumePresentation.AbsoluteCm3);
                        double v12Ptv = plan.GetVolumeAtDose(ptv, dv12Gy, VolumePresentation.AbsoluteCm3);
                        double v12Local = Math.Max(0, v12Ring - v12Ptv);
                        resultados.Add(new MetricaQA { Categoria = cat, Nombre = "V12Gy Local (tejido sano)", Valor = $"{v12Local:F2} cc", Referencia = "Predictor de radionecrosis (SRS 1 fracción)" });
                    }

                    resultados.Add(new MetricaQA { Categoria = cat, Nombre = "Distancia al Isocentro más Cercano", Valor = double.IsNaN(distCm) ? "N/D" : $"{distCm:F1} cm", Referencia = "Relevante en SRS multi-lesión (isocentro único)" });
                }
            }
            finally
            {
                // 5. LIMPIEZA: las estructuras auxiliares nunca deben quedar en el set de estructuras del paciente,
                // incluso si la creación de alguna de ellas falló a mitad de camino.
                if (ringV100A != null) ss.RemoveStructure(ringV100A);
                if (ringV100B != null) ss.RemoveStructure(ringV100B);
                if (ringV50A != null) ss.RemoveStructure(ringV50A);
                if (ringV50B != null) ss.RemoveStructure(ringV50B);
                if (dummy != null) ss.RemoveStructure(dummy);
            }

            // 6. LANZAR INTERFAZ
            var reportView = new ReporteView();
            reportView.SetData(context.Patient.LastName + ", " + context.Patient.FirstName, plan.Id, resultados);

            window.Content = reportView;
            window.Title = $"QA Report - {context.Patient.Id}";
            window.Width = 680;
            window.Height = 720;
        }

        // Elimina una estructura auxiliar residual de una ejecución previa fallida, si existe.
        private static void RemoveIfExists(StructureSet ss, string id)
        {
            var existente = ss.Structures.FirstOrDefault(s => s.Id == id);
            if (existente != null) ss.RemoveStructure(existente);
        }

        // Dosis de prescripción local de un PTV: si existe un Reference Point con el mismo Id y una
        // dosis límite total definida, se usa esa (planes SIB con dosis distinta por lesión);
        // en caso contrario, se usa la Rx global del plan.
        private static double GetLocalRxCgy(PlanSetup plan, Structure ptv, double planRxCgy)
        {
            foreach (ReferencePoint rp in plan.ReferencePoints.Where(r => r.Id == ptv.Id))
            {
                if (rp.TotalDoseLimit.ToString() != "N/A")
                {
                    double rxCgy = rp.TotalDoseLimit.Dose;
                    if (rp.TotalDoseLimit.Unit == DoseValue.DoseUnit.Gy) rxCgy *= 100.0;
                    return rxCgy;
                }
            }
            return planRxCgy;
        }

        // Compara dos volúmenes redondeados a una décima de cc; una diferencia indica que la isodosis
        // se extiende más allá del anillo más pequeño (posible dose bridging con una lesión vecina).
        private static bool VolumesDiffer(double a, double b)
        {
            return Math.Round(a, 1) != Math.Round(b, 1);
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

        // Helper: formatea un valor numérico como "N/D" si es NaN (fuera de rango del DVH o no calculable),
        // en vez de mostrar un número engañoso.
        private static string FormatValor(double value, string format, string unidad)
        {
            if (double.IsNaN(value)) return "N/D";
            return string.IsNullOrEmpty(unidad) ? value.ToString(format) : $"{value.ToString(format)} {unidad}";
        }
    }
}
