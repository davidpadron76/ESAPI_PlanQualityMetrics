using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace VMS.TPS
{
    // Clase auxiliar para los datos de la tabla
    public class MetricaQA
    {
        public string Nombre { get; set; }
        public string Valor { get; set; }
        public string Referencia { get; set; }
    }

    public partial class ReporteView : UserControl
    {
        private List<MetricaQA> _metricas;

        public ReporteView()
        {
            InitializeComponent();
        }

        // Este método recibe los datos desde tu Script principal
        public void SetData(string patientName, string planId, List<MetricaQA> resultados)
        {
            txtPatientName.Text = $"Paciente: {patientName}";
            txtPlanInfo.Text = $"Plan ID: {planId}";
            _metricas = resultados;

            // Llenamos la tabla visual con la lista de datos
            gridResultados.ItemsSource = _metricas;
        }

        // Lógica del botón Copiar
        private void BtnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (_metricas == null || _metricas.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"REPORT QA - {txtPatientName.Text}");
            sb.AppendLine("------------------------------------------------");
            foreach (var item in _metricas)
            {
                // Formato tabulado para pegar en Excel
                sb.AppendLine($"{item.Nombre}\t{item.Valor}\t[{item.Referencia}]");
            }
            
            try
            {
                Clipboard.SetText(sb.ToString());
                MessageBox.Show("Reporte copiado al portapapeles.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al copiar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}