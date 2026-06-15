# REPO_TREE

Note: generated from the current source tree. Excludes `.git`, `.vs`, `.tmp`, `publish`, `bin`, and `obj` directories.

```text
launcher/ : Repository root.
├─ Launcher.App/
│  ├─ Animations/
│  │  └─ GridLengthAnimation.cs : Custom animation type: GridLengthAnimation.
│  ├─ Assets/
│  │  └─ Icons/
│  │     ├─ account_page/
│  │     │  ├─ account_page_add_account.svg : Account-page icon asset: account_page_add_account.
│  │     │  ├─ account_page_add_account_dialog_offline_user.svg : Account-page icon asset: account_page_add_account_dialog_offline_user.
│  │     │  └─ account_page_add_account_dialog_online_user.svg : Account-page icon asset: account_page_add_account_dialog_online_user.
│  │     ├─ block/
│  │     │  ├─ Anvil.png : Minecraft block asset: Anvil.
│  │     │  ├─ Beacon_block.png : Minecraft block asset: Beacon_block.
│  │     │  ├─ craftingtable_block.png : Minecraft block asset: craftingtable_block.
│  │     │  ├─ diamond_block.png : Minecraft block asset: diamond_block.
│  │     │  ├─ dirt_block.png : Minecraft block asset: dirt_block.
│  │     │  ├─ Furnace_block.png : Minecraft block asset: Furnace_block.
│  │     │  ├─ grass_block.png : Minecraft block asset: grass_block.
│  │     │  ├─ note_block.png : Minecraft block asset: note_block.
│  │     │  ├─ redstone_block.png : Minecraft block asset: redstone_block.
│  │     │  ├─ redstone_lamp.png : Minecraft block asset: redstone_lamp.
│  │     │  ├─ stone_block.png : Minecraft block asset: stone_block.
│  │     │  └─ TNT_block.png : Minecraft block asset: TNT_block.
│  │     ├─ general/
│  │     │  ├─ general_all_application.svg : General icon asset: general_all_application.
│  │     │  ├─ general_arrow_left.svg : General icon asset: general_arrow_left.
│  │     │  ├─ general_attention.svg : General icon asset: general_attention.
│  │     │  ├─ general_copy.svg : General icon asset: general_copy.
│  │     │  ├─ general_delete.svg : General icon asset: general_delete.
│  │     │  ├─ general_edit.svg : General icon asset: general_edit.
│  │     │  ├─ general_extention.svg : General icon asset: general_extention.
│  │     │  ├─ general_external-web.svg : General icon asset: general_external-web.
│  │     │  ├─ general_passed.svg : General icon asset: general_passed.
│  │     │  ├─ general_pin.svg : General icon asset: general_pin.
│  │     │  └─ general_search.svg : General icon asset: general_search.
│  │     ├─ instance_download_page/
│  │     │  ├─ release.svg : Download-page icon asset: release.
│  │     │  └─ snapshot.svg : Download-page icon asset: snapshot.
│  │     ├─ home.svg : Home icon asset.
│  │     ├─ main_menu_account.svg : Main-menu icon asset: main_menu_account.
│  │     ├─ main_menu_expand.svg : Main-menu icon asset: main_menu_expand.
│  │     ├─ main_menu_fold.svg : Main-menu icon asset: main_menu_fold.
│  │     ├─ main_menu_install.svg : Main-menu icon asset: main_menu_install.
│  │     ├─ main_menu_instance_download.svg : Main-menu icon asset: main_menu_instance_download.
│  │     ├─ main_menu_instance_setting.svg : Main-menu icon asset: main_menu_instance_setting.
│  │     ├─ main_menu_launch.svg : Main-menu icon asset: main_menu_launch.
│  │     ├─ main_menu_library.svg : Main-menu icon asset: main_menu_library.
│  │     ├─ main_menu_setting.svg : Main-menu icon asset: main_menu_setting.
│  │     └─ README.md : Notes for icon assets.
│  ├─ Behaviors/
│  │  ├─ ProgressBarAnimation.cs : Attached behavior or visual helper: ProgressBarAnimation.
│  │  ├─ RoundedClip.cs : Attached behavior or visual helper: RoundedClip.
│  │  ├─ SmoothScrollBehavior.cs : Attached behavior or visual helper: SmoothScrollBehavior.
│  │  ├─ VerticalEdgeOpacityMask.cs : Attached behavior or visual helper: VerticalEdgeOpacityMask.
│  │  └─ VirtualizedListItemStateBehavior.cs : Attached behavior or visual helper: VirtualizedListItemStateBehavior.
│  ├─ Controls/
│  │  ├─ AnimatedComboBox.cs : Custom control or control helper type: AnimatedComboBox.
│  │  ├─ BackdropBlurBorder.cs : Custom control or control helper type: BackdropBlurBorder.
│  │  ├─ DialogHost.xaml : Reusable control view: DialogHost.
│  │  ├─ DialogHost.xaml.cs : Code-behind for reusable control: DialogHost.
│  │  ├─ ListPageFrame.xaml : Reusable control view: ListPageFrame.
│  │  ├─ ListPageFrame.xaml.cs : Code-behind for reusable control: ListPageFrame.
│  │  ├─ ListPageItemButton.xaml : Reusable control view: ListPageItemButton.
│  │  ├─ ListPageItemButton.xaml.cs : Code-behind for reusable control: ListPageItemButton.
│  │  ├─ SecondaryMenuFrame.xaml : Reusable control view: SecondaryMenuFrame.
│  │  ├─ SecondaryMenuFrame.xaml.cs : Code-behind for reusable control: SecondaryMenuFrame.
│  │  ├─ SecondaryMenuOptionButton.xaml : Reusable control view: SecondaryMenuOptionButton.
│  │  ├─ SecondaryMenuOptionButton.xaml.cs : Code-behind for reusable control: SecondaryMenuOptionButton.
│  │  └─ SvgIcon.cs : Custom control or control helper type: SvgIcon.
│  ├─ Converters/
│  │  ├─ AccountKindTextConverter.cs : Binding value converter: AccountKindTextConverter.
│  │  ├─ BooleanToMenuTextVisibilityConverter.cs : Binding value converter: BooleanToMenuTextVisibilityConverter.
│  │  ├─ CapeStateTextConverter.cs : Binding value converter: CapeStateTextConverter.
│  │  ├─ MinimumScrollThumbViewportConverter.cs : Binding value converter: MinimumScrollThumbViewportConverter.
│  │  ├─ PageTitleConverter.cs : Binding value converter: PageTitleConverter.
│  │  ├─ PageVisibilityConverter.cs : Binding value converter: PageVisibilityConverter.
│  │  └─ UuidTextConverter.cs : Binding value converter: UuidTextConverter.
│  ├─ Models/
│  │  ├─ AccountSkinModelOption.cs : UI model: AccountSkinModelOption.
│  │  ├─ AccountTypeOption.cs : UI model: AccountTypeOption.
│  │  ├─ NavigationCatalog.cs : UI model: NavigationCatalog.
│  │  ├─ NavigationItem.cs : UI model: NavigationItem.
│  │  └─ OfflineUuidModeOption.cs : UI model: OfflineUuidModeOption.
│  ├─ Resources/
│  │  ├─ Strings.cs : Handwritten resource wrapper used by C# and XAML.
│  │  ├─ Strings.resx : User-visible string resources.
│  │  └─ ThemeResources.xaml : Theme resource dictionary for colors, radius, shadows, spacing, and tokens.
│  ├─ Services/
│  │  ├─ AccountDialogService.cs : UI coordination or desktop service implementation: AccountDialogService.
│  │  ├─ AcrylicWindow.cs : UI coordination or desktop service implementation: AcrylicWindow.
│  │  ├─ BlurEffectWarmup.cs : UI coordination or desktop service implementation: BlurEffectWarmup.
│  │  ├─ ClipboardService.cs : UI coordination or desktop service implementation: ClipboardService.
│  │  ├─ DialogOverlayService.cs : UI coordination or desktop service implementation: DialogOverlayService.
│  │  ├─ DownloadStepTransitionCoordinator.cs : UI coordination or desktop service implementation: DownloadStepTransitionCoordinator.
│  │  ├─ FilePickerService.cs : UI coordination or desktop service implementation: FilePickerService.
│  │  ├─ HomePageViewModelFactory.cs : UI coordination or desktop service implementation: HomePageViewModelFactory.
│  │  ├─ IAccountDialogService.cs : UI service interface: IAccountDialogService.
│  │  ├─ IClipboardService.cs : UI service interface: IClipboardService.
│  │  ├─ IFilePickerService.cs : UI service interface: IFilePickerService.
│  │  ├─ IHomePageViewModelFactory.cs : UI service interface: IHomePageViewModelFactory.
│  │  ├─ ImmediateUiDispatcher.cs : UI service interface: ImmediateUiDispatcher.
│  │  ├─ IStatusService.cs : UI service interface: IStatusService.
│  │  ├─ IUiDispatcher.cs : UI service interface: IUiDispatcher.
│  │  ├─ IWindowService.cs : UI service interface: IWindowService.
│  │  ├─ NavigationMenuAnimationService.cs : UI coordination or desktop service implementation: NavigationMenuAnimationService.
│  │  ├─ PageTransitionService.cs : UI coordination or desktop service implementation: PageTransitionService.
│  │  ├─ StatusService.cs : UI coordination or desktop service implementation: StatusService.
│  │  ├─ WindowService.cs : UI coordination or desktop service implementation: WindowService.
│  │  └─ WpfUiDispatcher.cs : UI coordination or desktop service implementation: WpfUiDispatcher.
│  ├─ Styles/
│  │  ├─ ControlStyles.Buttons.xaml : Style resource dictionary: ControlStyles.Buttons.
│  │  ├─ ControlStyles.Dialogs.xaml : Style resource dictionary: ControlStyles.Dialogs.
│  │  ├─ ControlStyles.Inputs.xaml : Style resource dictionary: ControlStyles.Inputs.
│  │  ├─ ControlStyles.Lists.xaml : Style resource dictionary: ControlStyles.Lists.
│  │  ├─ ControlStyles.Navigation.xaml : Style resource dictionary: ControlStyles.Navigation.
│  │  ├─ ControlStyles.Page.xaml : Style resource dictionary: ControlStyles.Page.
│  │  ├─ ControlStyles.Scroll.xaml : Style resource dictionary: ControlStyles.Scroll.
│  │  └─ ControlStyles.xaml : Style resource dictionary: ControlStyles.
│  ├─ Utilities/
│  │  └─ VisualTreeSearch.cs : UI helper utility: VisualTreeSearch.
│  ├─ ViewModels/
│  │  ├─ AccountAppearanceViewModel.cs : Page, child, or helper ViewModel: AccountAppearanceViewModel.
│  │  ├─ AccountDialogConstants.cs : Page, child, or helper ViewModel: AccountDialogConstants.
│  │  ├─ AccountDialogText.cs : Page, child, or helper ViewModel: AccountDialogText.
│  │  ├─ AccountDialogViewModel.cs : Page, child, or helper ViewModel: AccountDialogViewModel.
│  │  ├─ AccountListViewModel.cs : Page, child, or helper ViewModel: AccountListViewModel.
│  │  ├─ AccountNameValidator.cs : Page, child, or helper ViewModel: AccountNameValidator.
│  │  ├─ AccountOfflineUuidViewModel.cs : Page, child, or helper ViewModel: AccountOfflineUuidViewModel.
│  │  ├─ AccountPageViewModel.cs : Page, child, or helper ViewModel: AccountPageViewModel.
│  │  ├─ AccountSkinModelDialogViewModel.cs : Page, child, or helper ViewModel: AccountSkinModelDialogViewModel.
│  │  ├─ AccountSkinModelOptionFactory.cs : Page, child, or helper ViewModel: AccountSkinModelOptionFactory.
│  │  ├─ AccountTypeOptionFactory.cs : Page, child, or helper ViewModel: AccountTypeOptionFactory.
│  │  ├─ DownloadInstallProgress.cs : Page, child, or helper ViewModel: DownloadInstallProgress.
│  │  ├─ DownloadInstanceNameTracker.cs : Page, child, or helper ViewModel: DownloadInstanceNameTracker.
│  │  ├─ DownloadLoaderOption.cs : Page, child, or helper ViewModel: DownloadLoaderOption.
│  │  ├─ DownloadMinecraftVersionItem.cs : Page, child, or helper ViewModel: DownloadMinecraftVersionItem.
│  │  ├─ DownloadPageStep.cs : Page, child, or helper ViewModel: DownloadPageStep.
│  │  ├─ DownloadPageViewModel.cs : Page, child, or helper ViewModel: DownloadPageViewModel.
│  │  ├─ DownloadTasksPageViewModel.cs : Page, child, or helper ViewModel: DownloadTasksPageViewModel.
│  │  ├─ DownloadVersionCategory.cs : Page, child, or helper ViewModel: DownloadVersionCategory.
│  │  ├─ DownloadVersionFilter.cs : Page, child, or helper ViewModel: DownloadVersionFilter.
│  │  ├─ GameManagementViewModel.cs : Page, child, or helper ViewModel: GameManagementViewModel.
│  │  ├─ GameSettingsInstanceCategory.cs : Page, child, or helper ViewModel: GameSettingsInstanceCategory.
│  │  ├─ GameSettingsInstanceFilter.cs : Page, child, or helper ViewModel: GameSettingsInstanceFilter.
│  │  ├─ GameSettingsInstanceItem.cs : Page, child, or helper ViewModel: GameSettingsInstanceItem.
│  │  ├─ GameSettingsPageViewModel.cs : Page, child, or helper ViewModel: GameSettingsPageViewModel.
│  │  ├─ HomeLaunchGameListViewModel.cs : Page, child, or helper ViewModel: HomeLaunchGameListViewModel.
│  │  ├─ HomeLaunchInstanceItem.cs : Page, child, or helper ViewModel: HomeLaunchInstanceItem.
│  │  ├─ HomePageViewModel.cs : Page, child, or helper ViewModel: HomePageViewModel.
│  │  ├─ InstanceManagementViewModel.cs : Page, child, or helper ViewModel: InstanceManagementViewModel.
│  │  ├─ LoaderSelectionViewModel.cs : Page, child, or helper ViewModel: LoaderSelectionViewModel.
│  │  ├─ LocalModsViewModel.cs : Page, child, or helper ViewModel: LocalModsViewModel.
│  │  ├─ MainViewModel.cs : Page, child, or helper ViewModel: MainViewModel.
│  │  ├─ MinecraftVersionIconResolver.cs : Page, child, or helper ViewModel: MinecraftVersionIconResolver.
│  │  ├─ ModrinthSearchViewModel.cs : Page, child, or helper ViewModel: ModrinthSearchViewModel.
│  │  └─ ObservableCollectionExtensions.cs : Page, child, or helper ViewModel: ObservableCollectionExtensions.
│  ├─ Views/
│  │  ├─ AccountDetailsView.xaml : Account page details view.
│  │  ├─ AccountDetailsView.xaml.cs : Code-behind for UI-only coordination: AccountDetailsView.
│  │  ├─ AccountMenuView.xaml : Account page left menu view.
│  │  ├─ AccountMenuView.xaml.cs : Code-behind for UI-only coordination: AccountMenuView.
│  │  ├─ AccountPageView.xaml : Account page shell view.
│  │  ├─ AccountPageView.xaml.cs : Code-behind for UI-only coordination: AccountPageView.
│  │  ├─ AddAccountDialogView.xaml : Add account dialog view.
│  │  ├─ AddAccountDialogView.xaml.cs : Code-behind for UI-only coordination: AddAccountDialogView.
│  │  ├─ DeleteAccountDialogView.xaml : Delete account dialog view.
│  │  ├─ DeleteAccountDialogView.xaml.cs : Code-behind for UI-only coordination: DeleteAccountDialogView.
│  │  ├─ DownloadInstanceOptionsView.xaml : Download instance options view.
│  │  ├─ DownloadInstanceOptionsView.xaml.cs : Code-behind for UI-only coordination: DownloadInstanceOptionsView.
│  │  ├─ DownloadPageView.xaml : Download page layout.
│  │  ├─ DownloadPageView.xaml.cs : Code-behind for UI-only coordination: DownloadPageView.
│  │  ├─ DownloadVersionListView.xaml : Download version list view.
│  │  ├─ DownloadVersionListView.xaml.cs : Code-behind for UI-only coordination: DownloadVersionListView.
│  │  ├─ GameSettingsPageView.xaml : Instance settings page layout.
│  │  ├─ GameSettingsPageView.xaml.cs : Code-behind for UI-only coordination: GameSettingsPageView.
│  │  ├─ GeneralPageView.xaml : General settings page layout.
│  │  ├─ GeneralPageView.xaml.cs : Code-behind for UI-only coordination: GeneralPageView.
│  │  ├─ HomeLaunchGameListView.xaml : Home instance list view.
│  │  ├─ HomeLaunchGameListView.xaml.cs : Code-behind for UI-only coordination: HomeLaunchGameListView.
│  │  ├─ HomeLaunchPanelView.xaml : Home launch panel view.
│  │  ├─ HomeLaunchPanelView.xaml.cs : Code-behind for UI-only coordination: HomeLaunchPanelView.
│  │  ├─ HomePageView.xaml : Home page layout.
│  │  ├─ HomePageView.xaml.cs : Code-behind for UI-only coordination: HomePageView.
│  │  ├─ InstallPageView.xaml : Install page layout.
│  │  ├─ InstallPageView.xaml.cs : Code-behind for UI-only coordination: InstallPageView.
│  │  ├─ RenameAccountDialogView.xaml : Rename account dialog view.
│  │  ├─ RenameAccountDialogView.xaml.cs : Code-behind for UI-only coordination: RenameAccountDialogView.
│  │  ├─ SkinModelDialogView.xaml : Skin model selection dialog view.
│  │  └─ SkinModelDialogView.xaml.cs : Code-behind for UI-only coordination: SkinModelDialogView.
│  ├─ App.xaml : WPF application resource entry.
│  ├─ App.xaml.cs : Application startup and DI composition root.
│  ├─ AssemblyInfo.cs : App assembly metadata.
│  ├─ Launcher.App.csproj : WPF app project file for UI, ViewModels, styles, and resources.
│  ├─ MainWindow.xaml : Main window layout and page host.
│  └─ MainWindow.xaml.cs : Main window UI coordination, animation, and host attach logic.
├─ Launcher.Application/
│  ├─ Accounts/
│  │  ├─ AccountCapeOption.cs : Account application type: AccountCapeOption.
│  │  ├─ AccountMapper.cs : Account application type: AccountMapper.
│  │  ├─ AccountStore.cs : Account application type: AccountStore.
│  │  ├─ IAccountStore.cs : Account application interface: IAccountStore.
│  │  ├─ ILaunchAccountSessionService.cs : Account application interface: ILaunchAccountSessionService.
│  │  ├─ IMicrosoftAccountService.cs : Account application interface: IMicrosoftAccountService.
│  │  ├─ IMinecraftSkinFileValidator.cs : Account application interface: IMinecraftSkinFileValidator.
│  │  ├─ IOfflineAccountUuidService.cs : Account application interface: IOfflineAccountUuidService.
│  │  ├─ LaunchAccountSession.cs : Account application type: LaunchAccountSession.
│  │  ├─ LaunchAccountSessionException.cs : Account exception type: LaunchAccountSessionException.
│  │  ├─ LauncherAccount.cs : Account application type: LauncherAccount.
│  │  ├─ MicrosoftAccountNameChangeException.cs : Account exception type: MicrosoftAccountNameChangeException.
│  │  ├─ MicrosoftAccountNameChangeFailureReason.cs : Account application type: MicrosoftAccountNameChangeFailureReason.
│  │  ├─ MicrosoftAccountProfileRefreshException.cs : Account exception type: MicrosoftAccountProfileRefreshException.
│  │  ├─ MicrosoftAccountSkinUpdateException.cs : Account exception type: MicrosoftAccountSkinUpdateException.
│  │  ├─ MinecraftSkinFileValidationResult.cs : Account application type: MinecraftSkinFileValidationResult.
│  │  └─ MinecraftSkinModel.cs : Account application type: MinecraftSkinModel.
│  ├─ DependencyInjection/
│  │  └─ ServiceCollectionExtensions.cs : Application layer DI registration entry.
│  ├─ Repositories/
│  │  └─ IGameInstanceRepository.cs : Repository interface: IGameInstanceRepository.
│  ├─ Services/
│  │  ├─ GameInstanceService.cs : Application service implementation: GameInstanceService.
│  │  ├─ IGameInstanceService.cs : Application service interface: IGameInstanceService.
│  │  ├─ IGameVersionService.cs : Application service interface: IGameVersionService.
│  │  ├─ ILauncherStateMonitor.cs : Application service interface: ILauncherStateMonitor.
│  │  ├─ ILaunchService.cs : Application service interface: ILaunchService.
│  │  ├─ ILoaderProvider.cs : Application service interface: ILoaderProvider.
│  │  ├─ IModrinthService.cs : Application service interface: IModrinthService.
│  │  ├─ IModService.cs : Application service interface: IModService.
│  │  └─ ISettingsService.cs : Application service interface: ISettingsService.
│  └─ Launcher.Application.csproj : Application project file for business interfaces and services.
├─ Launcher.Domain/
│  ├─ Models/
│  │  ├─ GameInstance.cs : Domain model or enum: GameInstance.
│  │  ├─ LauncherAccountRecord.cs : Domain model or enum: LauncherAccountRecord.
│  │  ├─ LauncherCapeRecord.cs : Domain model or enum: LauncherCapeRecord.
│  │  ├─ LauncherDefaults.cs : Domain model or enum: LauncherDefaults.
│  │  ├─ LauncherProgress.cs : Domain model or enum: LauncherProgress.
│  │  ├─ LauncherSettings.cs : Domain model or enum: LauncherSettings.
│  │  ├─ LoaderKind.cs : Domain model or enum: LoaderKind.
│  │  ├─ LoaderVersionInfo.cs : Domain model or enum: LoaderVersionInfo.
│  │  ├─ LocalMod.cs : Domain model or enum: LocalMod.
│  │  ├─ MinecraftVersionInfo.cs : Domain model or enum: MinecraftVersionInfo.
│  │  ├─ ModrinthModels.cs : Domain model or enum: ModrinthModels.
│  │  └─ OfflineUuidGenerationMode.cs : Domain model or enum: OfflineUuidGenerationMode.
│  └─ Launcher.Domain.csproj : Domain project file for pure domain models.
├─ Launcher.Infrastructure/
│  ├─ Accounts/
│  │  ├─ AccountAvatarService.cs : Account infrastructure implementation or error type: AccountAvatarService.
│  │  ├─ LaunchAccountSessionService.cs : Account infrastructure implementation or error type: LaunchAccountSessionService.
│  │  ├─ MicrosoftAccountFactory.cs : Account infrastructure implementation or error type: MicrosoftAccountFactory.
│  │  ├─ MicrosoftAccountService.cs : Account infrastructure implementation or error type: MicrosoftAccountService.
│  │  ├─ MicrosoftAuthProvider.cs : Account infrastructure implementation or error type: MicrosoftAuthProvider.
│  │  ├─ MicrosoftLoginResult.cs : Account infrastructure implementation or error type: MicrosoftLoginResult.
│  │  ├─ MinecraftAccountHelpers.cs : Account infrastructure implementation or error type: MinecraftAccountHelpers.
│  │  ├─ MinecraftCapeService.cs : Account infrastructure implementation or error type: MinecraftCapeService.
│  │  ├─ MinecraftProfileClient.cs : Account infrastructure implementation or error type: MinecraftProfileClient.
│  │  ├─ MinecraftProfileErrorKind.cs : Account infrastructure implementation or error type: MinecraftProfileErrorKind.
│  │  ├─ MinecraftProfileModels.cs : Account infrastructure implementation or error type: MinecraftProfileModels.
│  │  ├─ MinecraftProfileRequestException.cs : Account infrastructure implementation or error type: MinecraftProfileRequestException.
│  │  ├─ MinecraftSkinFileValidator.cs : Account infrastructure implementation or error type: MinecraftSkinFileValidator.
│  │  ├─ MinecraftSkinService.cs : Account infrastructure implementation or error type: MinecraftSkinService.
│  │  └─ OfflineAccountUuidService.cs : Account infrastructure implementation or error type: OfflineAccountUuidService.
│  ├─ DependencyInjection/
│  │  └─ ServiceCollectionExtensions.cs : Infrastructure layer DI registration entry.
│  ├─ FileSystem/
│  │  ├─ LauncherStateMonitor.cs : File-system implementation: LauncherStateMonitor.
│  │  └─ ModService.cs : File-system implementation: ModService.
│  ├─ Minecraft/
│  │  ├─ DownloadSpeedTrackingGameInstaller.cs : Minecraft integration implementation: DownloadSpeedTrackingGameInstaller.
│  │  ├─ GameVersionService.cs : Minecraft integration implementation: GameVersionService.
│  │  ├─ LaunchService.cs : Minecraft integration implementation: LaunchService.
│  │  ├─ LoaderProviders.cs : Minecraft integration implementation: LoaderProviders.
│  │  └─ VanillaVersionIsolator.cs : Minecraft integration implementation: VanillaVersionIsolator.
│  ├─ Modrinth/
│  │  ├─ Dto/
│  │  │  └─ ModrinthApiModels.cs : Modrinth API DTO definitions.
│  │  └─ ModrinthService.cs : Modrinth integration implementation: ModrinthService.
│  ├─ Persistence/
│  │  ├─ JsonGameInstanceRepository.cs : Persistence implementation: JsonGameInstanceRepository.
│  │  └─ JsonSettingsService.cs : Persistence implementation: JsonSettingsService.
│  ├─ Properties/
│  │  └─ AssemblyInfo.cs : Infrastructure assembly metadata.
│  ├─ Launcher.Infrastructure.csproj : Infrastructure project file for external implementations.
│  └─ LauncherPathProvider.cs : Launcher path provider for workspace and runtime locations.
├─ Launcher.Tests/
│  ├─ AccountAvatarServiceTests.cs : Test suite for AccountAvatarService behavior and edge cases.
│  ├─ AccountPageViewModelTests.cs : Test suite for AccountPageViewModel behavior and edge cases.
│  ├─ AccountStoreTests.cs : Test suite for AccountStore behavior and edge cases.
│  ├─ CaptureHandler.cs : HTTP capture handler for tests.
│  ├─ DownloadPageViewModelTests.cs : Test suite for DownloadPageViewModel behavior and edge cases.
│  ├─ DownloadTasksPageViewModelTests.cs : Test suite for DownloadTasksPageViewModel behavior and edge cases.
│  ├─ FakeGameInstanceService.cs : Fake test double for GameInstanceService.
│  ├─ FakeGameVersionService.cs : Fake test double for GameVersionService.
│  ├─ FakeLoaderProvider.cs : Fake test double for LoaderProvider.
│  ├─ FakeOfflineAccountUuidService.cs : Fake test double for OfflineAccountUuidService.
│  ├─ GameInstanceServiceTests.cs : Test suite for GameInstanceService behavior and edge cases.
│  ├─ GameSettingsPageViewModelTests.cs : Test suite for GameSettingsPageViewModel behavior and edge cases.
│  ├─ HomePageViewModelTests.cs : Test suite for HomePageViewModel behavior and edge cases.
│  ├─ Launcher.Tests.csproj : Test project file.
│  ├─ MainViewModelTests.cs : Test suite for MainViewModel behavior and edge cases.
│  ├─ MinecraftProfileClientTests.cs : Test suite for MinecraftProfileClient behavior and edge cases.
│  ├─ MinecraftSkinFileValidatorTests.cs : Test suite for MinecraftSkinFileValidator behavior and edge cases.
│  ├─ ModrinthServiceTests.cs : Test suite for ModrinthService behavior and edge cases.
│  ├─ ModServiceTests.cs : Test suite for ModService behavior and edge cases.
│  ├─ OfflineAccountUuidServiceTests.cs : Test suite for OfflineAccountUuidService behavior and edge cases.
│  ├─ ResourceDictionaryTests.cs : Test suite for ResourceDictionary behavior and edge cases.
│  ├─ SettingsServiceTests.cs : Test suite for SettingsService behavior and edge cases.
│  ├─ TestAsync.cs : Async test helpers.
│  ├─ TestSettingsService.cs : Test implementation of settings service.
│  └─ TestTempDirectory.cs : Temporary directory helper for tests.
├─ .gitignore : Git ignore rules.
├─ AGENTS.md : Workspace architecture, MVVM, layering, UI text, test, and guardrail rules.
├─ Directory.Build.props : Shared MSBuild properties for the solution.
├─ Launcher.sln : Solution entry that wires App, Application, Domain, Infrastructure, and Tests.
├─ NuGet.config : NuGet source and package restore configuration.
└─ RunLauncher.bat : Batch script to launch the app locally.
```
