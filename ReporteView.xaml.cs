using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VMS.TPS
{
    // Clase auxiliar para los datos de la tabla
    public class MetricaQA
    {
        public string Categoria { get; set; }
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

            // Agrupamos por categoría para separar visualmente Prescripción / Volúmenes / Dosis / Índices / Complejidad
            ICollectionView vista = CollectionViewSource.GetDefaultView(_metricas);
            vista.GroupDescriptions.Clear();
            vista.GroupDescriptions.Add(new PropertyGroupDescription("Categoria"));

            gridResultados.ItemsSource = vista;
        }

        // Lógica del botón Copiar
        private void BtnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (_metricas == null || _metricas.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"REPORT QA - {txtPatientName.Text}");
            sb.AppendLine($"{txtPlanInfo.Text}");
            sb.AppendLine("------------------------------------------------");

            string categoriaActual = null;
            foreach (var item in _metricas)
            {
                if (item.Categoria != categoriaActual)
                {
                    categoriaActual = item.Categoria;
                    sb.AppendLine();
                    sb.AppendLine($"[{categoriaActual}]");
                }
                // Formato tabulado para pegar en Excel
                sb.AppendLine($"{item.Nombre}\t{item.Valor}\t{item.Referencia}");
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