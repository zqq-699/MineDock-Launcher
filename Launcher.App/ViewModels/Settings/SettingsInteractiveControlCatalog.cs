using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Settings;

public static class SettingsInteractiveControlCatalog
{
    public static IReadOnlyList<SettingsInteractiveControlItem> Create()
    {
        return
        [
            new(Strings.Settings_ControlSecondaryMenuButton, Strings.Settings_ControlCategoryNavigation),
            new(Strings.Settings_ControlListPageItemButton, Strings.Settings_ControlCategoryNavigation),
            new(Strings.Settings_ControlDialogButton, Strings.Settings_ControlCategoryButton),
            new(Strings.Settings_ControlPrimaryButton, Strings.Settings_ControlCategoryButton),
            new(Strings.Settings_ControlDangerButton, Strings.Settings_ControlCategoryButton),
            new(Strings.Settings_ControlInlineIconButton, Strings.Settings_ControlCategoryButton),
            new(Strings.Settings_ControlSwitch, Strings.Settings_ControlCategoryToggle),
            new(Strings.Settings_ControlSlider, Strings.Settings_ControlCategoryInput),
            new(Strings.Settings_ControlComboBox, Strings.Settings_ControlCategorySelection),
            new(Strings.Settings_ControlTextBox, Strings.Settings_ControlCategoryInput),
            new(Strings.Settings_ControlMultilineTextBox, Strings.Settings_ControlCategoryInput),
            new(Strings.Settings_ControlSearchBox, Strings.Settings_ControlCategoryInput),
            new(Strings.Settings_ControlChoiceList, Strings.Settings_ControlCategorySelection),
            new(Strings.Settings_ControlVirtualizedList, Strings.Settings_ControlCategorySelection)
        ];
    }
}
