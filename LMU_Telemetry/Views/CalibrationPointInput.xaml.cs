using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace LMU_Telemetry.Views;

public partial class CalibrationPointInput : Window
{
    public string PointName { get; private set; } = "";
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    
    private double _mapX;
    private double _mapY;
    
    public CalibrationPointInput(double mapX, double mapY)
    {
        InitializeComponent();
        
        _mapX = mapX;
        _mapY = mapY;
        
        MapCoordsText.Text = $"Map X: {mapX:F1}, Y: {mapY:F1}";
        NameTextBox.Focus();
    }
    
    private void AddPoint_Click(object sender, RoutedEventArgs e)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("Please enter a name for this calibration point.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (!double.TryParse(LatTextBox.Text, out var lat))
        {
            MessageBox.Show("Please enter a valid latitude value.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (!double.TryParse(LonTextBox.Text, out var lon))
        {
            MessageBox.Show("Please enter a valid longitude value.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        PointName = NameTextBox.Text;
        Latitude = lat;
        Longitude = lon;
        
        DialogResult = true;
        Close();
    }
    
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
