// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;

namespace Retro87.Views;

/// <summary>
/// The selected key (or Super Button) of the Mapeamento tab: name, where it lives in the device, the
/// target picker and the restore buttons. It is used twice by <see cref="MainWindow"/> — beside the
/// board in a wide window and inside the tab's scroll region in a narrow one — so it lives in its own
/// file instead of being duplicated in the XAML.
/// </summary>
public partial class SelectedKeyPanel : UserControl
{
    public SelectedKeyPanel() => InitializeComponent();
}
