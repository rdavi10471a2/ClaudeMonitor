# Source Map Smoke Summary

Generated: 2026-05-16T00:04:18.0931452-05:00
Requested path: `EditorSurface\EditorSurfaceControl.cs`
Requested scope: `file`

Reported scope: `file`
Watched project: `C:\Schema Studio - DBV2`
File count: `1`
Symbol count: `9`

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
