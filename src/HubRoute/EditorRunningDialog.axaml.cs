using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HubRoute;

/// <summary>Warns that an already running Unity Editor cannot inherit a newly injected proxy.</summary>
public partial class EditorRunningDialog : Window
{
    /// <summary>Initializes the dialog for the Avalonia runtime loader and design tools.</summary>
    public EditorRunningDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initializes the dialog and displays the number of detected Editor processes.</summary>
    public EditorRunningDialog(int editorProcessCount) : this()
    {
        EditorCountText.Text = $"检测到 {editorProcessCount} 个 Unity Editor 进程。";
    }

    /// <summary>Moves keyboard focus to the safe cancellation action when the dialog opens.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CancelButton.Focus();
    }

    /// <summary>Closes the dialog without launching Unity Hub.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    /// <summary>Confirms that Unity Hub should launch while leaving the existing Editor untouched.</summary>
    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
