using Avalonia.Controls;
using System;

namespace OptiscalerClient.Helpers
{
    /// <summary>
    /// Automatically dims the owner window's <see cref="DimOverlayName"/> border when a dialog
    /// opens on top of it, and restores it when the dialog closes.
    ///
    /// Usage: call <see cref="Register"/> from any dialog window's constructor after
    /// InitializeComponent(). The owner window must have a Border named "DimOverlay" somewhere
    /// in its visual tree (typically as the last child of its root Panel or Grid).
    /// </summary>
    public static class DialogDimHelper
    {
        private const string DimOverlayName = "DimOverlay";

        /// <summary>
        /// Registers dim/undim hooks on the given dialog window.
        /// When the window opens it shows the DimOverlay in its owner; when it closes it hides it.
        /// </summary>
        public static void Register(Window dialog)
        {
            dialog.Opened += OnDialogOpened;
            dialog.Closed += OnDialogClosed;
        }

        /// <summary>
        /// Hides the owner's DimOverlay immediately. Call this at the very start of a close
        /// animation so the backdrop disappears in sync with the dialog fade-out, not after it.
        /// </summary>
        public static void HideDimNow(Window dialog)
            => SetOverlayVisible(dialog.Owner as Window, false);

        private static void OnDialogOpened(object? sender, EventArgs e)
        {
            if (sender is Window dialog)
                SetOverlayVisible(dialog.Owner as Window, true);
        }

        private static void OnDialogClosed(object? sender, EventArgs e)
        {
            if (sender is Window dialog)
                SetOverlayVisible(dialog.Owner as Window, false);
        }

        private static void SetOverlayVisible(Window? owner, bool visible)
        {
            if (owner == null) return;
            var overlay = owner.FindControl<Border>(DimOverlayName);
            if (overlay != null)
                overlay.IsVisible = visible;
        }
    }
}
