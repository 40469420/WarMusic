using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NAudio.CoreAudioApi;
using WarMusic.Audio;
using WarMusic.Models;
using WarMusic.Services;

namespace WarMusic;

public partial class SetupWindow : Window
{
    readonly MainViewModel model;
    readonly Device?[] choices = new Device?[3];
    int step;
    bool updating;

    public SetupWindow(MainViewModel model)
    {
        this.model = model;
        InitializeComponent();
        choices[0] = model.SelectedMic;
        choices[1] = model.SelectedMonitor;
        choices[2] = model.SelectedCable;
        Loaded += (_, _) => WindowTheme.Apply(new WindowInteropHelper(this).Handle);
        Closed += (_, _) => model.DismissSetup();
        ShowStep();
    }

    void ShowStep()
    {
        StepProgress.Value = step + 1;
        StepTitle.Text = new[] { "1. Your microphone", "2. Your headphones", "3. Your virtual cable", "4. Finish in your game" }[step];
        StepDescription.Text = step == 3 ? "One mixed input for voice, music, and local sounds." : "Choose where your audio goes. Nothing starts playing during setup.";
        BackButton.IsEnabled = step > 0;
        NextButton.Content = step == 3 ? "Save setup" : "Next";
        DevicePage.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        GamePage.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Validation.Text = "";
        if (step == 3) { CableSummary.Text = choices[2]?.Name; NextButton.IsEnabled = true; return; }
        DeviceLabel.Text = new[] { "Physical microphone", "Private listening output", "Cable playback endpoint" }[step];
        DeviceHint.Text = new[] { "Choose the microphone you speak into, not a virtual cable.", "Choose your headphones. Keep this separate from the cable sent to your game.", "Choose CABLE Input for VB-CABLE. If you have several cables, use the matching Output in your game. Install a cable first if this list is empty." }[step];
        InstallCable.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        LoadDevices();
    }

    void LoadDevices()
    {
        updating = true;
        try
        {
            var devices = Devices.List(step == 0 ? DataFlow.Capture : DataFlow.Render);
            if (step == 0) devices = devices.Where(d => !d.Name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)).ToList();
            if (step == 2) devices = devices.Where(d => Devices.IsVirtualCablePlayback(d.Name)).ToList();
            DeviceChoice.ItemsSource = devices;
            DeviceChoice.SelectedItem = devices.FirstOrDefault(d => d.Id == choices[step]?.Id);
            choices[step] = DeviceChoice.SelectedItem as Device;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            DeviceChoice.ItemsSource = null;
            choices[step] = null;
            Validation.Text = "Could not read audio devices. Check Windows audio settings, then refresh.";
            Store.Log(ex.ToString());
        }
        finally { updating = false; }
        ValidateChoice();
    }

    void ValidateChoice()
    {
        bool sameOutput = step == 2 && choices[2] != null && choices[2]?.Id == choices[1]?.Id;
        NextButton.IsEnabled = choices[step] != null && !sameOutput;
        if (sameOutput) Validation.Text = "The cable and headphones must be different outputs. Go back to choose your headphones.";
        else if (choices[step] != null) Validation.Text = "";
    }

    void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || step >= 3) return;
        choices[step] = DeviceChoice.SelectedItem as Device;
        ValidateChoice();
    }
    void RefreshDevices(object sender, RoutedEventArgs e) => LoadDevices();
    internal void VerifySetupScreens(string directory)
    {
        var saved = (model.Profile.MicId, model.Profile.MonitorId, model.Profile.CableId);
        Show();
        for (int index = 0; index < 4; index++)
        {
            step = index;
            ShowStep();
            if (step < 3)
            {
                DeviceChoice.SelectedItem = null;
                if (NextButton.IsEnabled) throw new InvalidOperationException("Setup allowed an empty device choice.");
                var device = new Device("setup-preview-" + step, step switch { 0 => "Your microphone", 1 => "Your headphones", _ => "CABLE Input (VB-Audio Virtual Cable)" });
                DeviceChoice.ItemsSource = new[] { device };
                DeviceChoice.SelectedItem = device;
                if (!NextButton.IsEnabled) throw new InvalidOperationException("Setup rejected a valid device choice.");
                if (step == 2)
                {
                    var headphones = choices[1];
                    choices[1] = device;
                    ValidateChoice();
                    if (NextButton.IsEnabled) throw new InvalidOperationException("Setup allowed headphones and cable to match.");
                    choices[1] = headphones;
                    ValidateChoice();
                }
            }
            UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"setup-{step}.png"));
            encoder.Save(file);
        }
        Close();
        if (saved != (model.Profile.MicId, model.Profile.MonitorId, model.Profile.CableId) || model.Engine.Running)
            throw new InvalidOperationException("Setup changed the route before saving.");
    }
    void GetCable(object sender, RoutedEventArgs e) => model.CableCommand.Execute(null);
    void Back(object sender, RoutedEventArgs e) { if (step > 0) { step--; ShowStep(); } }
    void Later(object sender, RoutedEventArgs e) { model.DismissSetup(); Close(); }
    void Next(object sender, RoutedEventArgs e)
    {
        if (step < 3) { step++; ShowStep(); return; }
        // Refresh the model before applying drafts: disconnected devices must not be saved as ready.
        model.RefreshCommand.Execute(null);
        if (!model.Microphones.Any(d => d.Id == choices[0]?.Id) ||
            !model.Outputs.Any(d => d.Id == choices[1]?.Id) || !model.Outputs.Any(d => d.Id == choices[2]?.Id))
        {
            Validation.Text = "A selected device is no longer available. Go back and refresh your choices.";
            return;
        }
        model.CompleteSetup(choices[0]!, choices[1]!, choices[2]!);
        DialogResult = true;
    }
}
