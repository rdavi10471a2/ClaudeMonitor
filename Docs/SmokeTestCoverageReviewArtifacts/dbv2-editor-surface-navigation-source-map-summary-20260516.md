# Source Map Smoke Summary

Generated: 2026-05-16T07:19:51.3942459-05:00
Requested path: `EditorSurface`
Requested scope: `folder`
Requested mode: `navigation`

Reported scope: `folder`
Reported mode: `navigation`
Watched project: ``
File count: `7`
Symbol count: `93`
Estimated token proxy: `5382`
Budget limit: `20000`

## EditorSurface\CommandDispatcher.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `4`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `CommandDispatcher` | `3-18` | `` | `` | `` | `` | `public class CommandDispatcher { }` |
| `field` | `_commands` | `5-5` | `` | `` | `` | `` | `private readonly Dictionary<string, Action> _commands = new();` |
| `method` | `Register` | `7-10` | `` | `` | `` | `` | `public void Register(string commandName, Action handler);` |
| `method` | `Dispatch` | `12-16` | `` | `` | `` | `` | `public void Dispatch(string commandName);` |

## EditorSurface\CommsnfBarControl.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `6`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `CommandBarControl` | `5-45` | `` | `` | `` | `UserControl` | `public partial class CommandBarControl : UserControl { }` |
| `field` | `_menuStrip` | `7-7` | `` | `` | `` | `` | `private readonly MenuStrip _menuStrip;` |
| `field` | `_dispatcher` | `8-8` | `` | `` | `` | `` | `private readonly CommandDispatcher _dispatcher;` |
| `constructor` | `CommandBarControl` | `10-17` | `` | `` | `` | `` | `public CommandBarControl(CommandDispatcher dispatcher);` |
| `property` | `Renderer` | `19-25` | `` | `` | `Browsable, DesignerSerializationVisibility` | `` | `public ToolStripRenderer Renderer { get; set; }` |
| `method` | `AddMenuItem` | `27-44` | `` | `` | `` | `` | `public void AddMenuItem(string menuName, string itemText, string commandName);` |

## EditorSurface\EditorSurfaceControl.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `9`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `EditorSurfaceControl` | `6-131` | `` | `` | `` | `UserControl` | `public partial class EditorSurfaceControl : UserControl { }` |
| `field` | `_mainSplitter` | `8-8` | `` | `` | `` | `` | `private SplitContainer _mainSplitter;` |
| `field` | `_dgvColumns` | `9-9` | `` | `` | `` | `` | `private DataGridView _dgvColumns;` |
| `field` | `_pgDetails` | `10-10` | `` | `` | `` | `` | `private PropertyGrid _pgDetails;` |
| `field` | `_currentColumns` | `11-11` | `` | `` | `` | `` | `private BindingList<BaseTableColumnDefinition> _currentColumns;` |
| `field` | `_splitterInitialized` | `14-14` | `` | `` | `` | `` | `private bool _splitterInitialized = false;` |
| `constructor` | `EditorSurfaceControl` | `16-34` | `` | `` | `` | `` | `public EditorSurfaceControl();` |
| `method` | `LoadTable` | `36-59` | `` | `` | `` | `` | `public void LoadTable(BaseTableDefinition table);` |
| `method` | `InitializeComponent` | `61-130` | `` | `` | `` | `` | `private void InitializeComponent();` |

## EditorSurface\ExplorerControl.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `43`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `enum` | `MetadataType` | `13-13` | `` | `` | `` | `` | `public enum MetadataType {     Database,     Table,     DomainObject }` |
| `class` | `MetadataEventArgs` | `15-25` | `` | `` | `` | `EventArgs` | `public class MetadataEventArgs : EventArgs { }` |
| `property` | `SelectedItem` | `17-17` | `` | `` | `` | `` | `public object SelectedItem { get; }` |
| `property` | `Type` | `18-18` | `` | `` | `` | `` | `public MetadataType Type { get; }` |
| `constructor` | `MetadataEventArgs` | `20-24` | `` | `` | `` | `` | `public MetadataEventArgs(object item, MetadataType type);` |
| `class` | `ExplorerControl` | `27-529` | `` | `` | `` | `UserControl` | `public partial class ExplorerControl : UserControl { }` |
| `event` | `MetadataSelected` | `29-29` | `` | `` | `` | `` | `public event EventHandler<MetadataEventArgs> MetadataSelected;` |
| `field` | `_lstDatabases` | `31-31` | `` | `` | `` | `` | `private ListBox _lstDatabases;` |
| `field` | `_lstTables` | `32-32` | `` | `` | `` | `` | `private ListBox _lstTables;` |
| `field` | `_lstEntities` | `33-33` | `` | `` | `` | `` | `private ListBox _lstEntities;` |
| `field` | `_cmbMetadataView` | `34-34` | `` | `` | `` | `` | `private ComboBox _cmbMetadataView;` |
| `field` | `_panelTables` | `35-35` | `` | `` | `` | `` | `private Panel _panelTables;` |
| `field` | `_panelEntities` | `36-36` | `` | `` | `` | `` | `private Panel _panelEntities;` |
| `field` | `_lblDatabases` | `37-37` | `` | `` | `` | `` | `private Label _lblDatabases;` |
| `field` | `_lblMetadataView` | `38-38` | `` | `` | `` | `` | `private Label _lblMetadataView;` |
| `field` | `_btnSaveAll` | `39-39` | `` | `` | `` | `` | `private Button _btnSaveAll;` |
| `field` | `_tableContextMenu` | `41-41` | `` | `` | `` | `` | `private ContextMenuStrip _tableContextMenu;` |
| `field` | `_databaseContextMenu` | `42-42` | `` | `` | `` | `` | `private ContextMenuStrip _databaseContextMenu;` |
| `field` | `_dbRepo` | `44-44` | `` | `` | `` | `` | `private readonly DatabaseRepository _dbRepo = new();` |
| `field` | `_tableRepo` | `45-45` | `` | `` | `` | `` | `private readonly BaseTableRepository _tableRepo = new();` |
| `field` | `_scriptRepo` | `46-46` | `` | `` | `` | `` | `private readonly TargetScriptRepository _scriptRepo = new();` |
| `field` | `_lastSelectedTable` | `48-48` | `` | `` | `` | `` | `private BaseTableDefinition _lastSelectedTable;` |
| `field` | `_lastTableIndex` | `49-49` | `` | `` | `` | `` | `private int _lastTableIndex = -1;` |
| `field` | `_lastDatabaseIndex` | `50-50` | `` | `` | `` | `` | `private int _lastDatabaseIndex = -1;` |
| `field` | `_lastComboIndex` | `51-51` | `` | `` | `` | `` | `private int _lastComboIndex = 0;` |
| `field` | `_isInternalChange` | `52-52` | `` | `` | `` | `` | `private bool _isInternalChange = false;` |
| `constructor` | `ExplorerControl` | `54-59` | `` | `` | `` | `` | `public ExplorerControl();` |
| `method` | `InitializeContextMenus` | `61-94` | `` | `` | `` | `` | `private void InitializeContextMenus();` |
| `method` | `ExportTableItem_Click` | `96-99` | `` | `` | `` | `` | `private void ExportTableItem_Click(object? sender, EventArgs e);` |
| `method` | `ImportTableDetails` | `101-104` | `` | `` | `` | `` | `private void ImportTableDetails(object? sender, EventArgs e);` |
| `method` | `EditItem_Click` | `106-116` | `` | `` | `` | `` | `private void EditItem_Click(object sender, EventArgs e);` |
| `method` | `RelationshipItem_Click` | `118-128` | `` | `` | `` | `` | `private void RelationshipItem_Click(object sender, EventArgs e);` |
| `method` | `EditTableList_Click` | `130-139` | `` | `` | `` | `` | `private void EditTableList_Click(object sender, EventArgs e);` |
| `method` | `ScriptItem_Click` | `141-159` | `` | `` | `` | `` | `private void ScriptItem_Click(object sender, EventArgs e);` |
| `method` | `RefreshData` | `161-223` | `` | `` | `` | `` | `public void RefreshData();` |
| `method` | `InitializeLayout` | `225-366` | `` | `` | `` | `` | `private void InitializeLayout();` |
| `method` | `CheckForDirtyChanges` | `368-381` | `` | `` | `` | `` | `private bool CheckForDirtyChanges();` |
| `method` | `LoadTableColumns` | `383-466` | `` | `` | `` | `` | `private void LoadTableColumns();` |
| `method` | `ExecuteManualSave` | `469-498` | `` | `` | `` | `` | `private void ExecuteManualSave();` |
| `method` | `HandleRightClickSelect` | `500-507` | `` | `` | `` | `` | `private void HandleRightClickSelect(ListBox lb, MouseEventArgs e);` |

## EditorSurface\LogControl.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `14`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `LogControl` | `6-162` | `` | `` | `FileVersion, AIHistory, AIInstructions` | `UserControl` | `[FileVersion("1.0")] [AIHistory("1.0", "Initial AI metadata entry for LogControl workflow test.")] [AIInstructions("Added initial FileVersion and AI metadata for LogControl test cycle.", AICommandStatus.Completed)] public partial class LogControl : UserControl { }` |
| `field` | `_logBox` | `11-11` | `` | `` | `` | `` | `private RichTextBox _logBox;` |
| `field` | `_btnClear` | `12-12` | `` | `` | `` | `` | `private Button _btnClear;` |
| `field` | `_btnCopy` | `13-13` | `` | `` | `` | `` | `private Button _btnCopy;` |
| `field` | `_mainLayout` | `14-14` | `` | `` | `` | `` | `private TableLayoutPanel _mainLayout;` |
| `field` | `_buttonContainer` | `15-15` | `` | `` | `` | `` | `private FlowLayoutPanel _buttonContainer;` |
| `constructor` | `LogControl` | `17-25` | `` | `` | `` | `` | `public LogControl();` |
| `method` | `InitializeWelcomeMessage` | `27-34` | `` | `` | `` | `` | `private void InitializeWelcomeMessage();` |
| `method` | `InitializeLayout` | `36-88` | `` | `` | `` | `` | `private void InitializeLayout();` |
| `method` | `CreateButton` | `90-101` | `` | `` | `` | `` | `private Button CreateButton(string text);` |
| `method` | `ClearLog` | `103-112` | `` | `` | `` | `` | `private void ClearLog();` |
| `method` | `CopyToClipboard` | `114-120` | `` | `` | `` | `` | `private void CopyToClipboard();` |
| `method` | `OnLogMessage` | `122-146` | `` | `` | `` | `` | `private void OnLogMessage(object sender, LogMessageEventArgs e);` |
| `method` | `AppendLog` | `148-161` | `` | `` | `` | `` | `private void AppendLog(string message, Color color, DateTime timestamp);` |

## EditorSurface\SchemaViewer.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `14`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `SchemaViewer` | `9-171` | `` | `` | `` | `UserControl` | `public partial class SchemaViewer : UserControl { }` |
| `field` | `_dockPanel` | `11-11` | `` | `` | `` | `` | `private readonly DockPanel _dockPanel;` |
| `field` | `_dispatcher` | `12-12` | `` | `` | `` | `` | `private readonly CommandDispatcher _dispatcher;` |
| `field` | `_commandBar` | `13-13` | `` | `` | `` | `` | `private CommandBarControl _commandBar;` |
| `field` | `_explorerPane` | `15-15` | `` | `` | `` | `` | `private DockContent _explorerPane;` |
| `field` | `_logPane` | `16-16` | `` | `` | `` | `` | `private DockContent _logPane;` |
| `field` | `_editorPane` | `17-17` | `` | `` | `` | `` | `private DockContent _editorPane;` |
| `constructor` | `SchemaViewer` | `19-45` | `` | `` | `` | `` | `public SchemaViewer();` |
| `method` | `RegisterUICommands` | `47-61` | `` | `` | `` | `` | `private void RegisterUICommands();` |
| `method` | `ShowPane` | `63-103` | `` | `` | `` | `` | `private void ShowPane(ref DockContent pane, string title, DockState state, UserControl content);` |
| `method` | `InitializeWorkspace` | `105-114` | `` | `` | `` | `` | `private void InitializeWorkspace();` |
| `method` | `EditConnectionString` | `116-128` | `` | `` | `` | `` | `private void EditConnectionString();` |
| `method` | `ConfigureDatabases` | `130-156` | `` | `` | `` | `` | `private void ConfigureDatabases();` |
| `method` | `RefreshExplorer` | `158-170` | `` | `` | `` | `` | `private void RefreshExplorer();` |

## EditorSurface\TextLengthAttribute.cs

Hash: ``
Parse status: `ok`
Diagnostics: `0`
Symbols: `3`

| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `class` | `TextLengthAttribute` | `3-12` | `` | `` | `AttributeUsage` | `Attribute` | `[AttributeUsage(AttributeTargets.Property)] internal class TextLengthAttribute : Attribute { }` |
| `property` | `MaxLength` | `6-6` | `` | `` | `` | `` | `public int MaxLength { get; }` |
| `constructor` | `TextLengthAttribute` | `8-11` | `` | `` | `` | `` | `public TextLengthAttribute(int maxLength);` |
