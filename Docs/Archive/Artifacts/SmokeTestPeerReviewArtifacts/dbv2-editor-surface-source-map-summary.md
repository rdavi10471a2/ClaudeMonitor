# Source Map Smoke Summary

Generated: 2026-05-15T23:22:26.1120895-05:00
Requested path: `EditorSurface`
Requested scope: `folder`

Reported scope: `folder`
Watched project: `C:\Schema Studio - DBV2`
File count: `7`
Symbol count: `93`

## EditorSurface\CommandDispatcher.cs

Hash: `aa7507e78b604bc2c7d80ea022eded3be72d88bfa8052ff74f32e4353b6be75d`
Parse status: `ok`
Diagnostics: `0`
Symbols: `4`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `CommandDispatcher` | `3-18` | `` | `` | `` | `` | `public class CommandDispatcher { }` |
| `field` | `_commands` | `5-5` | `Dictionary<string, Action>` | `` | `` | `` | `private readonly Dictionary<string, Action> _commands = new();` |
| `method` | `Register` | `7-10` | `void` | `string, Action` | `` | `` | `public void Register(string commandName, Action handler);` |
| `method` | `Dispatch` | `12-16` | `void` | `string` | `` | `` | `public void Dispatch(string commandName);` |

## EditorSurface\CommsnfBarControl.cs

Hash: `affb5eaa9ac7253d7be1c11ea0d8d302bfec5ac772bab6ead0fec9263bc06c8c`
Parse status: `ok`
Diagnostics: `0`
Symbols: `6`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `CommandBarControl` | `5-45` | `` | `` | `` | `UserControl` | `public partial class CommandBarControl : UserControl { }` |
| `field` | `_menuStrip` | `7-7` | `MenuStrip` | `` | `` | `` | `private readonly MenuStrip _menuStrip;` |
| `field` | `_dispatcher` | `8-8` | `CommandDispatcher` | `` | `` | `` | `private readonly CommandDispatcher _dispatcher;` |
| `constructor` | `CommandBarControl` | `10-17` | `` | `CommandDispatcher` | `` | `` | `public CommandBarControl(CommandDispatcher dispatcher);` |
| `property` | `Renderer` | `19-25` | `ToolStripRenderer` | `` | `Browsable, DesignerSerializationVisibility` | `` | `[Browsable(false)] [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public ToolStripRenderer Renderer { get; set; }` |
| `method` | `AddMenuItem` | `27-44` | `void` | `string, string, string` | `` | `` | `public void AddMenuItem(string menuName, string itemText, string commandName);` |

## EditorSurface\EditorSurfaceControl.cs

Hash: `cb723a4deb7e40d056accc11ef5b5f88961dc67b8cd55d431e17ca4c26daef29`
Parse status: `ok`
Diagnostics: `0`
Symbols: `9`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `EditorSurfaceControl` | `6-131` | `` | `` | `` | `UserControl` | `public partial class EditorSurfaceControl : UserControl { }` |
| `field` | `_mainSplitter` | `8-8` | `SplitContainer` | `` | `` | `` | `private SplitContainer _mainSplitter;` |
| `field` | `_dgvColumns` | `9-9` | `DataGridView` | `` | `` | `` | `private DataGridView _dgvColumns;` |
| `field` | `_pgDetails` | `10-10` | `PropertyGrid` | `` | `` | `` | `private PropertyGrid _pgDetails;` |
| `field` | `_currentColumns` | `11-11` | `BindingList<BaseTableColumnDefinition>` | `` | `` | `` | `private BindingList<BaseTableColumnDefinition> _currentColumns;` |
| `field` | `_splitterInitialized` | `14-14` | `bool` | `` | `` | `` | `private bool _splitterInitialized = false;` |
| `constructor` | `EditorSurfaceControl` | `16-34` | `` | `` | `` | `` | `public EditorSurfaceControl();` |
| `method` | `LoadTable` | `36-59` | `void` | `BaseTableDefinition` | `` | `` | `public void LoadTable(BaseTableDefinition table);` |
| `method` | `InitializeComponent` | `61-130` | `void` | `` | `` | `` | `private void InitializeComponent();` |

## EditorSurface\ExplorerControl.cs

Hash: `370f6df4cb1aa3b13e313260a921357212e7f2502c55780cc34739aa3abb8dda`
Parse status: `ok`
Diagnostics: `0`
Symbols: `43`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `enum` | `MetadataType` | `13-13` | `` | `` | `` | `` | `public enum MetadataType {     Database,     Table,     DomainObject }` |
| `class` | `MetadataEventArgs` | `15-25` | `` | `` | `` | `EventArgs` | `public class MetadataEventArgs : EventArgs { }` |
| `property` | `SelectedItem` | `17-17` | `object` | `` | `` | `` | `public object SelectedItem { get; }` |
| `property` | `Type` | `18-18` | `MetadataType` | `` | `` | `` | `public MetadataType Type { get; }` |
| `constructor` | `MetadataEventArgs` | `20-24` | `` | `object, MetadataType` | `` | `` | `public MetadataEventArgs(object item, MetadataType type);` |
| `class` | `ExplorerControl` | `27-529` | `` | `` | `` | `UserControl` | `public partial class ExplorerControl : UserControl { }` |
| `event` | `MetadataSelected` | `29-29` | `EventHandler<MetadataEventArgs>` | `` | `` | `` | `public event EventHandler<MetadataEventArgs> MetadataSelected;` |
| `field` | `_lstDatabases` | `31-31` | `ListBox` | `` | `` | `` | `private ListBox _lstDatabases;` |
| `field` | `_lstTables` | `32-32` | `ListBox` | `` | `` | `` | `private ListBox _lstTables;` |
| `field` | `_lstEntities` | `33-33` | `ListBox` | `` | `` | `` | `private ListBox _lstEntities;` |
| `field` | `_cmbMetadataView` | `34-34` | `ComboBox` | `` | `` | `` | `private ComboBox _cmbMetadataView;` |
| `field` | `_panelTables` | `35-35` | `Panel` | `` | `` | `` | `private Panel _panelTables;` |
| `field` | `_panelEntities` | `36-36` | `Panel` | `` | `` | `` | `private Panel _panelEntities;` |
| `field` | `_lblDatabases` | `37-37` | `Label` | `` | `` | `` | `private Label _lblDatabases;` |
| `field` | `_lblMetadataView` | `38-38` | `Label` | `` | `` | `` | `private Label _lblMetadataView;` |
| `field` | `_btnSaveAll` | `39-39` | `Button` | `` | `` | `` | `private Button _btnSaveAll;` |
| `field` | `_tableContextMenu` | `41-41` | `ContextMenuStrip` | `` | `` | `` | `private ContextMenuStrip _tableContextMenu;` |
| `field` | `_databaseContextMenu` | `42-42` | `ContextMenuStrip` | `` | `` | `` | `private ContextMenuStrip _databaseContextMenu;` |
| `field` | `_dbRepo` | `44-44` | `DatabaseRepository` | `` | `` | `` | `private readonly DatabaseRepository _dbRepo = new();` |
| `field` | `_tableRepo` | `45-45` | `BaseTableRepository` | `` | `` | `` | `private readonly BaseTableRepository _tableRepo = new();` |
| `field` | `_scriptRepo` | `46-46` | `TargetScriptRepository` | `` | `` | `` | `private readonly TargetScriptRepository _scriptRepo = new();` |
| `field` | `_lastSelectedTable` | `48-48` | `BaseTableDefinition` | `` | `` | `` | `private BaseTableDefinition _lastSelectedTable;` |
| `field` | `_lastTableIndex` | `49-49` | `int` | `` | `` | `` | `private int _lastTableIndex = -1;` |
| `field` | `_lastDatabaseIndex` | `50-50` | `int` | `` | `` | `` | `private int _lastDatabaseIndex = -1;` |
| `field` | `_lastComboIndex` | `51-51` | `int` | `` | `` | `` | `private int _lastComboIndex = 0;` |
| `field` | `_isInternalChange` | `52-52` | `bool` | `` | `` | `` | `private bool _isInternalChange = false;` |
| `constructor` | `ExplorerControl` | `54-59` | `` | `` | `` | `` | `public ExplorerControl();` |
| `method` | `InitializeContextMenus` | `61-94` | `void` | `` | `` | `` | `private void InitializeContextMenus();` |
| `method` | `ExportTableItem_Click` | `96-99` | `void` | `object?, EventArgs` | `` | `` | `private void ExportTableItem_Click(object? sender, EventArgs e);` |
| `method` | `ImportTableDetails` | `101-104` | `void` | `object?, EventArgs` | `` | `` | `private void ImportTableDetails(object? sender, EventArgs e);` |
| `method` | `EditItem_Click` | `106-116` | `void` | `object, EventArgs` | `` | `` | `private void EditItem_Click(object sender, EventArgs e);` |
| `method` | `RelationshipItem_Click` | `118-128` | `void` | `object, EventArgs` | `` | `` | `private void RelationshipItem_Click(object sender, EventArgs e);` |
| `method` | `EditTableList_Click` | `130-139` | `void` | `object, EventArgs` | `` | `` | `private void EditTableList_Click(object sender, EventArgs e);` |
| `method` | `ScriptItem_Click` | `141-159` | `void` | `object, EventArgs` | `` | `` | `private void ScriptItem_Click(object sender, EventArgs e);` |
| `method` | `RefreshData` | `161-223` | `void` | `` | `` | `` | `public void RefreshData();` |
| `method` | `InitializeLayout` | `225-366` | `void` | `` | `` | `` | `private void InitializeLayout();` |
| `method` | `CheckForDirtyChanges` | `368-381` | `bool` | `` | `` | `` | `private bool CheckForDirtyChanges();` |
| `method` | `LoadTableColumns` | `383-466` | `void` | `` | `` | `` | `private void LoadTableColumns();` |
| `method` | `ExecuteManualSave` | `469-498` | `void` | `` | `` | `` | `private void ExecuteManualSave();` |
| `method` | `HandleRightClickSelect` | `500-507` | `void` | `ListBox, MouseEventArgs` | `` | `` | `private void HandleRightClickSelect(ListBox lb, MouseEventArgs e);` |

## EditorSurface\LogControl.cs

Hash: `cf63117b07d3f7e103e701e4c92c595e8741c88a5d0bcb6c108390f58ff8104b`
Parse status: `ok`
Diagnostics: `0`
Symbols: `14`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `LogControl` | `6-162` | `` | `` | `FileVersion, AIHistory, AIInstructions` | `UserControl` | `[FileVersion("1.0")] [AIHistory("1.0", "Initial AI metadata entry for LogControl workflow test.")] [AIInstructions("Added initial FileVersion and AI metadata for LogControl test cycle.", AICommandStatus.Completed)] public partial class LogControl : UserControl { }` |
| `field` | `_logBox` | `11-11` | `RichTextBox` | `` | `` | `` | `private RichTextBox _logBox;` |
| `field` | `_btnClear` | `12-12` | `Button` | `` | `` | `` | `private Button _btnClear;` |
| `field` | `_btnCopy` | `13-13` | `Button` | `` | `` | `` | `private Button _btnCopy;` |
| `field` | `_mainLayout` | `14-14` | `TableLayoutPanel` | `` | `` | `` | `private TableLayoutPanel _mainLayout;` |
| `field` | `_buttonContainer` | `15-15` | `FlowLayoutPanel` | `` | `` | `` | `private FlowLayoutPanel _buttonContainer;` |
| `constructor` | `LogControl` | `17-25` | `` | `` | `` | `` | `public LogControl();` |
| `method` | `InitializeWelcomeMessage` | `27-34` | `void` | `` | `` | `` | `private void InitializeWelcomeMessage();` |
| `method` | `InitializeLayout` | `36-88` | `void` | `` | `` | `` | `private void InitializeLayout();` |
| `method` | `CreateButton` | `90-101` | `Button` | `string` | `` | `` | `private Button CreateButton(string text);` |
| `method` | `ClearLog` | `103-112` | `void` | `` | `` | `` | `private void ClearLog();` |
| `method` | `CopyToClipboard` | `114-120` | `void` | `` | `` | `` | `private void CopyToClipboard();` |
| `method` | `OnLogMessage` | `122-146` | `void` | `object, LogMessageEventArgs` | `` | `` | `private void OnLogMessage(object sender, LogMessageEventArgs e);` |
| `method` | `AppendLog` | `148-161` | `void` | `string, Color, DateTime` | `` | `` | `private void AppendLog(string message, Color color, DateTime timestamp);` |

## EditorSurface\SchemaViewer.cs

Hash: `f9a1d2d2dfb899164f6bc97bda6fe6426f8db74c7fb5c9d7b2e4e4a2e1c20341`
Parse status: `ok`
Diagnostics: `0`
Symbols: `14`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `SchemaViewer` | `9-171` | `` | `` | `` | `UserControl` | `public partial class SchemaViewer : UserControl { }` |
| `field` | `_dockPanel` | `11-11` | `DockPanel` | `` | `` | `` | `private readonly DockPanel _dockPanel;` |
| `field` | `_dispatcher` | `12-12` | `CommandDispatcher` | `` | `` | `` | `private readonly CommandDispatcher _dispatcher;` |
| `field` | `_commandBar` | `13-13` | `CommandBarControl` | `` | `` | `` | `private CommandBarControl _commandBar;` |
| `field` | `_explorerPane` | `15-15` | `DockContent` | `` | `` | `` | `private DockContent _explorerPane;` |
| `field` | `_logPane` | `16-16` | `DockContent` | `` | `` | `` | `private DockContent _logPane;` |
| `field` | `_editorPane` | `17-17` | `DockContent` | `` | `` | `` | `private DockContent _editorPane;` |
| `constructor` | `SchemaViewer` | `19-45` | `` | `` | `` | `` | `public SchemaViewer();` |
| `method` | `RegisterUICommands` | `47-61` | `void` | `` | `` | `` | `private void RegisterUICommands();` |
| `method` | `ShowPane` | `63-103` | `void` | `DockContent, string, DockState, UserControl` | `` | `` | `private void ShowPane(ref DockContent pane, string title, DockState state, UserControl content);` |
| `method` | `InitializeWorkspace` | `105-114` | `void` | `` | `` | `` | `private void InitializeWorkspace();` |
| `method` | `EditConnectionString` | `116-128` | `void` | `` | `` | `` | `private void EditConnectionString();` |
| `method` | `ConfigureDatabases` | `130-156` | `void` | `` | `` | `` | `private void ConfigureDatabases();` |
| `method` | `RefreshExplorer` | `158-170` | `void` | `` | `` | `` | `private void RefreshExplorer();` |

## EditorSurface\TextLengthAttribute.cs

Hash: `8b403a8b7102846e66153ab8e744f914f5fc0ad8cdcdf0e47ee0bbfcf9554347`
Parse status: `ok`
Diagnostics: `0`
Symbols: `3`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `TextLengthAttribute` | `3-12` | `` | `` | `AttributeUsage` | `Attribute` | `[AttributeUsage(AttributeTargets.Property)] internal class TextLengthAttribute : Attribute { }` |
| `property` | `MaxLength` | `6-6` | `int` | `` | `` | `` | `public int MaxLength { get; }` |
| `constructor` | `TextLengthAttribute` | `8-11` | `` | `int` | `` | `` | `public TextLengthAttribute(int maxLength);` |
