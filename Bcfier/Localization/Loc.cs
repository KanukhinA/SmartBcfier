using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using Bcfier.Data.Utils;

namespace Bcfier.Localization
{
    /// <summary>
    /// Локализация UI. По умолчанию — русский, переключение через настройки.
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceManager ResourceManager =
            new ResourceManager("Bcfier.Localization.Strings", typeof(Loc).Assembly);

        static Loc()
        {
            // Нельзя вызывать UserSettings/MessageBox из cctor: при {x:Static loc:...} это
            // даёт TypeInitializationException (реентрантный доступ к Loc).
            // Культуру из настроек применяет BcfierRevitHost / окна до InitializeComponent.
            try
            {
                ApplyCulture("ru");
            }
            catch
            {
                // Тип Loc должен остаться usable даже при сбое CultureInfo.
            }
        }

        public static string Get(string key)
        {
            return Get(key, Thread.CurrentThread.CurrentUICulture);
        }

        public static string Get(string key, CultureInfo culture)
        {
            try
            {
                return ResourceManager.GetString(key, culture ?? Thread.CurrentThread.CurrentUICulture) ?? key;
            }
            catch
            {
                return key;
            }
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>
        /// Склонение счётчика: ключи Prefix_one / Prefix_few / Prefix_many.
        /// </summary>
        public static string Plural(string keyPrefix, int count)
        {
            string suffix = PluralSuffix(count);
            return Format(keyPrefix + "_" + suffix, count);
        }

        private static string PluralSuffix(int count)
        {
            // Английский: 1 → one, иначе many
            if (!Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase))
                return count == 1 ? "one" : "many";

            // Русские формы: 1 вид, 2–4 вида, 5+ видов (+ исключения 11–14)
            int n = Math.Abs(count) % 100;
            int n1 = n % 10;
            if (n > 10 && n < 20)
                return "many";
            if (n1 > 1 && n1 < 5)
                return "few";
            if (n1 == 1)
                return "one";
            return "many";
        }

        public static void ApplyCultureFromSettings()
        {
            try
            {
                string lang = UserSettings.Get("Language");
                // Пустое значение в настройках — русский по умолчанию
                if (string.IsNullOrWhiteSpace(lang))
                    lang = "ru";

                ApplyCulture(lang);
            }
            catch
            {
                ApplyCulture("ru");
            }
        }

        public static void ApplyCulture(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = "ru";

            // en* → English; всё остальное → ru-RU
            CultureInfo culture = languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo("en")
                : CultureInfo.GetCultureInfo("ru-RU");

            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
        }

        public static string SettingsTitle => Get("SettingsTitle");
        public static string Save => Get("Save");
        public static string Cancel => Get("Cancel");
        public static string Close => Get("Close");
        public static string Add => Get("Add");
        public static string AddIssue => Get("AddIssue");
        public static string BatchAdd => Get("BatchAdd");
        public static string OpenBcf => Get("OpenBcf");
        public static string SaveBcf => Get("SaveBcf");
        public static string SaveAs => Get("SaveAs");
        public static string NewBcf => Get("NewBcf");
        public static string Merge => Get("Merge");
        public static string Settings => Get("Settings");
        public static string Help => Get("Help");
        public static string Language => Get("Language");
        public static string BcfWriteVersion => Get("BcfWriteVersion");
        public static string CreationAuthor => Get("CreationAuthor");
        public static string AssignedTo => Get("AssignedTo");
        public static string DueDate => Get("DueDate");
        public static string Priority => Get("Priority");
        public static string Labels => Get("Labels");
        public static string SelectAll => Get("SelectAll");
        public static string DeselectAll => Get("DeselectAll");
        public static string ElementsList => Get("ElementsList");
        public static string SelectInModel => Get("SelectInModel");
        public static string SelectAllInModel => Get("SelectAllInModel");
        public static string SelectInModelTip => Get("SelectInModelTip");
        public static string SelectAllInModelTip => Get("SelectAllInModelTip");
        public static string TooManyComponents => Get("TooManyComponents");
        public static string TooManyViewElements => Get("TooManyViewElements");
        public static string ManualElementIdsHint => Get("ManualElementIdsHint");
        public static string AddComment => Get("AddComment");
        public static string AddView => Get("AddView");
        public static string EditView => Get("EditView");
        public static string EditViewTip => Get("EditViewTip");
        public static string ViewEditSnapshotComment => Get("ViewEditSnapshotComment");
        public static string ViewEditElementsComment => Get("ViewEditElementsComment");
        public static string ViewEditSnapshotAndElementsComment => Get("ViewEditSnapshotAndElementsComment");
        public static string DeleteSelected => Get("DeleteSelected");
        public static string Browse => Get("Browse");
        public static string Annotate => Get("Annotate");
        public static string AnnotateSnapshot => Get("AnnotateSnapshot");
        public static string Remove => Get("Remove");
        public static string AuthorName => Get("AuthorName");
        public static string Russian => Get("Russian");
        public static string English => Get("English");
        public static string Yes => Get("Yes");
        public static string MaybeLater => Get("MaybeLater");
        public static string OnlyVisible => Get("OnlyVisible");
        public static string OnlySelected => Get("OnlySelected");
        public static string None => Get("None");
        public static string TopicStatuses => Get("TopicStatuses");
        public static string StatusNameColumn => Get("StatusNameColumn");
        public static string StatusColorColumn => Get("StatusColorColumn");
        public static string PickStatusColorTip => Get("PickStatusColorTip");
        public static string AddStatus => Get("AddStatus");
        public static string RemoveStatus => Get("RemoveStatus");
        public static string NewStatusName => Get("NewStatusName");
        public static string NewTypeName => Get("NewTypeName");
        public static string NewPriorityName => Get("NewPriorityName");
        public static string NewLabelName => Get("NewLabelName");
        public static string TopicTypes => Get("TopicTypes");
        public static string TopicType => Get("TopicType");
        public static string PrioritiesList => Get("PrioritiesList");
        public static string LabelsList => Get("LabelsList");
        public static string AssigneesList => Get("AssigneesList");
        public static string SearchIssues => Get("SearchIssues");
        public static string SearchIssuesTip => Get("SearchIssuesTip");
        public static string ActiveDocumentIssuesOnly => Get("ActiveDocumentIssuesOnly");
        public static string NewIssue => Get("NewIssue");
        public static string IssueTitle => Get("IssueTitle");
        public static string IssueTitleRequired => Get("IssueTitleRequired");
        public static string Description => Get("Description");
        public static string AddCommentPlaceholder => Get("AddCommentPlaceholder");
        public static string CommaSeparated => Get("CommaSeparated");
        public static string TabGeneral => Get("TabGeneral");
        public static string TabCatalog => Get("TabCatalog");
        public static string GroupApp => Get("GroupApp");
        public static string GroupBcf => Get("GroupBcf");
        public static string GroupTopicLists => Get("GroupTopicLists");
        public static string GroupSnapshot => Get("GroupSnapshot");
        public static string TabRevit => Get("TabRevit");
        public static string TabServer => Get("TabServer");
        public static string TabGoogleSheets => Get("TabGoogleSheets");
        public static string TabCustomFields => Get("TabCustomFields");
        public static string CustomFieldsCatalogHint => Get("CustomFieldsCatalogHint");
        public static string CustomFieldName => Get("CustomFieldName");
        public static string CustomFieldType => Get("CustomFieldType");
        public static string CustomFieldTypeText => Get("CustomFieldTypeText");
        public static string CustomFieldTypeDate => Get("CustomFieldTypeDate");
        public static string CustomFieldDuplicateName => Get("CustomFieldDuplicateName");
        public static string CustomFieldsAddDefinition => Get("CustomFieldsAddDefinition");
        public static string CustomFieldsRemoveDefinition => Get("CustomFieldsRemoveDefinition");
        public static string CustomFieldsAddToTopic => Get("CustomFieldsAddToTopic");
        public static string CustomFieldsAddTitle => Get("CustomFieldsAddTitle");
        public static string CustomFieldsManageTitle => Get("CustomFieldsManageTitle");
        public static string CustomFieldsAddHint => Get("CustomFieldsAddHint");
        public static string CustomFieldsAddHintReport => Get("CustomFieldsAddHintReport");
        public static string CustomFieldsManageHint => Get("CustomFieldsManageHint");
        public static string CustomFieldsGroupTitle => Get("CustomFieldsGroupTitle");
        public static string CustomFieldsPrompt => Get("CustomFieldsPrompt");
        public static string CustomFieldsPreviewTitle => Get("CustomFieldsPreviewTitle");
        public static string CustomFieldsPreviewHint => Get("CustomFieldsPreviewHint");
        public static string CustomFieldsPreviewScope => Get("CustomFieldsPreviewScope");
        public static string CustomFieldsPreviewShow => Get("CustomFieldsPreviewShow");
        public static string CustomFieldsPreviewHide => Get("CustomFieldsPreviewHide");
        public static string CustomFieldsNoneSelected => Get("CustomFieldsNoneSelected");
        public static string CustomFieldsNoDefinitions => Get("CustomFieldsNoDefinitions");
        public static string CustomFieldsValue => Get("CustomFieldsValue");
        public static string CustomFieldsReportLevel => Get("CustomFieldsReportLevel");
        public static string GroupSpService => Get("GroupSpService");
        public static string SpServiceBaseUrl => Get("SpServiceBaseUrl");
        public static string SpServiceLogin => Get("SpServiceLogin");
        public static string SpServicePassword => Get("SpServicePassword");
        public static string SpServiceProject => Get("SpServiceProject");
        public static string SpServicePickProjectTitle => Get("SpServicePickProjectTitle");
        public static string SpServicePickProjectHint => Get("SpServicePickProjectHint");
        public static string SpServiceSelectProject => Get("SpServiceSelectProject");
        public static string SpServicePickProjectConfirm => Get("SpServicePickProjectConfirm");
        public static string SpServiceReportName => Get("SpServiceReportName");
        public static string SpServiceReportNameRequired => Get("SpServiceReportNameRequired");
        public static string SpServiceNoProjects => Get("SpServiceNoProjects");
        public static string SpServiceModel => Get("SpServiceModel");
        public static string SpServiceSyncInterval => Get("SpServiceSyncInterval");
        public static string SpServiceTestConnection => Get("SpServiceTestConnection");
        public static string SpServiceTestHint => Get("SpServiceTestHint");
        public static string GroupGoogleSheets => Get("GroupGoogleSheets");
        public static string GoogleSheetsHint => Get("GoogleSheetsHint");
        public static string GoogleSheetsEnabled => Get("GoogleSheetsEnabled");
        public static string GoogleSheetsServiceAccountJson => Get("GoogleSheetsServiceAccountJson");
        public static string GoogleSheetsLoad => Get("GoogleSheetsLoad");
        public static string GoogleSheetsTest => Get("GoogleSheetsTest");
        public static string GoogleSheetsSave => Get("GoogleSheetsSave");
        public static string GoogleSheetsNeedProject => Get("GoogleSheetsNeedProject");
        public static string GoogleSheetsConfiguredAs => Get("GoogleSheetsConfiguredAs");
        public static string GoogleSheetsNotConfigured => Get("GoogleSheetsNotConfigured");
        public static string TableExportGoogleSheets => Get("TableExportGoogleSheets");
        public static string TableExportGoogleSheetsTip => Get("TableExportGoogleSheetsTip");
        public static string GoogleSheetsExportTitle => Get("GoogleSheetsExportTitle");
        public static string GoogleSheetsSpreadsheetId => Get("GoogleSheetsSpreadsheetId");
        public static string GoogleSheetsSheetName => Get("GoogleSheetsSheetName");
        public static string GoogleSheetsCreateNew => Get("GoogleSheetsCreateNew");
        public static string GoogleSheetsExportOk => Get("GoogleSheetsExportOk");
        public static string GoogleSheetsExportNeedServer => Get("GoogleSheetsExportNeedServer");
        public static string GoogleSheetsExportNeedModerator => Get("GoogleSheetsExportNeedModerator");
        public static string SpServicePickTitle => Get("SpServicePickTitle");
        public static string SpServicePickHint => Get("SpServicePickHint");
        public static string OpenFromDb => Get("OpenFromDb");
        public static string SendToDb => Get("SendToDb");
        public static string AutoSyncWithDb => Get("AutoSyncWithDb");
        public static string AutoSyncWithDbTip => Get("AutoSyncWithDbTip");
        public static string GroupView => Get("GroupView");
        public static string FirstAndLast => Get("FirstAndLast");
        public static string SnapshotsEditor => Get("SnapshotsEditor");
        public static string BrowseDoubleClick => Get("BrowseDoubleClick");
        public static string SnapshotEditorTip => Get("SnapshotEditorTip");
        public static string UseDefaultViewer => Get("UseDefaultViewer");
        public static string UseDefaultViewerTip => Get("UseDefaultViewerTip");
        public static string AlwaysNewRevitView => Get("AlwaysNewRevitView");
        public static string BcfCoordinateMode => Get("BcfCoordinateMode");
        public static string BcfCoordinateModeRevitZUp => Get("BcfCoordinateModeRevitZUp");
        public static string BcfCoordinateModeSourceYUp => Get("BcfCoordinateModeSourceYUp");
        public static string BcfCoordinateModeSourceXUp => Get("BcfCoordinateModeSourceXUp");
        public static string AttachElements => Get("AttachElements");
        public static string LoadLocalImage => Get("LoadLocalImage");
        public static string SelectLocalImage => Get("SelectLocalImage");
        public static string Path => Get("Path");
        public static string CommentOptional => Get("CommentOptional");
        public static string VerbalStatusOptional => Get("VerbalStatusOptional");
        public static string IncludeComment => Get("IncludeComment");
        public static string BrowseOrDropImage => Get("BrowseOrDropImage");
        public static string ViewElements => Get("ViewElements");
        public static string OriginatingSystemLabel => Get("OriginatingSystemLabel");
        public static string SelectModelElements => Get("SelectModelElements");
        public static string SelectModelElementsPrompt => Get("SelectModelElementsPrompt");
        public static string ViewHas3dInfo => Get("ViewHas3dInfo");
        public static string OpenViewTip => Get("OpenViewTip");
        public static string OpenViewIsolatedTip => Get("OpenViewIsolatedTip");
        public static string OpenViewButton => Get("OpenViewButton");
        public static string OpenViewIsolatedButton => Get("OpenViewIsolatedButton");
        public static string EnlargeView => Get("EnlargeView");
        public static string DeleteView => Get("DeleteView");
        public static string DeleteComment => Get("DeleteComment");
        public static string EditComment => Get("EditComment");
        public static string CommentEdited => Get("CommentEdited");
        public static string CommentDeletedFormat => Get("CommentDeletedFormat");
        public static string CopyComment => Get("CopyComment");
        public static string CopyCommentTip => Get("CopyCommentTip");
        public static string GeneralComments => Get("GeneralComments");
        public static string Status => Get("Status");
        public static string Error => Get("Error");
        public static string Warning => Get("Warning");
        public static string DragDropHint => Get("DragDropHint");
        public static string NewVersionTitle => Get("NewVersionTitle");
        public static string ColumnId => Get("ColumnId");
        public static string ColumnIfcGuid => Get("ColumnIfcGuid");
        public static string ComponentsNotFoundTitle => Get("ComponentsNotFoundTitle");
        public static string ColumnFamily => Get("ColumnFamily");
        public static string ColumnType => Get("ColumnType");
        public static string SelectedElements => Get("SelectedElements");
        public static string OtherElements => Get("OtherElements");
        public static string AvailableOnView => Get("AvailableOnView");
        public static string MoveToSelected => Get("MoveToSelected");
        public static string MoveToAvailable => Get("MoveToAvailable");
        public static string MoveAllToSelected => Get("MoveAllToSelected");
        public static string MoveAllToAvailable => Get("MoveAllToAvailable");
        public static string PlaceholderStatuses => Get("PlaceholderStatuses");
        public static string PlaceholderTypes => Get("PlaceholderTypes");
        public static string PlaceholderPriorities => Get("PlaceholderPriorities");
        public static string PlaceholderLabels => Get("PlaceholderLabels");
        public static string PlaceholderAssignees => Get("PlaceholderAssignees");
        public static string ProductName => Get("ProductName");
        public static string PanelLongDescription => Get("PanelLongDescription");
        public static string RibbonTooltip => Get("RibbonTooltip");
        public static string RibbonButton => Get("RibbonButton");
        public static string RibbonPanel => Get("RibbonPanel");

        public static string TableMode => Get("TableMode");
        public static string TableModeTip => Get("TableModeTip");
        public static string ClassicMode => Get("ClassicMode");
        public static string ClassicModeTip => Get("ClassicModeTip");
        public static string TableReportLabel => Get("TableReportLabel");
        public static string TableReportRenameTip => Get("TableReportRenameTip");
        public static string TableColumns => Get("TableColumns");
        public static string TableColumnsTip => Get("TableColumnsTip");
        public static string TableColumnsTitle => Get("TableColumnsTitle");
        public static string TableColumnsHint => Get("TableColumnsHint");
        public static string TableColumnMoveUp => Get("TableColumnMoveUp");
        public static string TableColumnMoveDown => Get("TableColumnMoveDown");
        public static string TableColumnCustomName => Get("TableColumnCustomName");
        public static string TableColumnCustomNameTip => Get("TableColumnCustomNameTip");
        public static string TableColumnVisibleHeader => Get("TableColumnVisibleHeader");
        public static string TableColumnDefaultHeader => Get("TableColumnDefaultHeader");
        public static string TableExportExcel => Get("TableExportExcel");
        public static string TableExportExcelTip => Get("TableExportExcelTip");
        public static string TableExportHtml => Get("TableExportHtml");
        public static string TableExportHtmlTip => Get("TableExportHtmlTip");
        public static string TableExportExcelFilter => Get("TableExportExcelFilter");
        public static string TableExportHtmlFilter => Get("TableExportHtmlFilter");
        public static string TableExportPdf => Get("TableExportPdf");
        public static string TableExportPdfTip => Get("TableExportPdfTip");
        public static string TableExportPdfFilter => Get("TableExportPdfFilter");
        public static string TableExportEmpty => Get("TableExportEmpty");
        public static string TableEmptyHint => Get("TableEmptyHint");
        public static string TableColStage => Get("TableColStage");
        public static string TableColCreationDate => Get("TableColCreationDate");
        public static string TableColModifiedAuthor => Get("TableColModifiedAuthor");
        public static string TableColModifiedDate => Get("TableColModifiedDate");
        public static string TableColIndex => Get("TableColIndex");
        public static string TableColGuid => Get("TableColGuid");
        public static string TableColDescriptionAndSnapshot => Get("TableColDescriptionAndSnapshot");
        public static string TableColTitleAndSnapshot => Get("TableColTitleAndSnapshot");
        public static string TableCommentUserColumn => Get("TableCommentUserColumn");
        public static string TableCommentGroupColumn => Get("TableCommentGroupColumn");
        public static string TableCommentScopeAll => Get("TableCommentScopeAll");
        public static string TableCommentScopeUser => Get("TableCommentScopeUser");
        public static string TableCommentScopeGroup => Get("TableCommentScopeGroup");
        public static string TableCommentDuplicateColumn => Get("TableCommentDuplicateColumn");
        public static string TableCommentRemoveColumn => Get("TableCommentRemoveColumn");
        public static string TableCommentGroupsTitle => Get("TableCommentGroupsTitle");
        public static string TableCommentGroupsHint => Get("TableCommentGroupsHint");
        public static string TableCommentAddGroup => Get("TableCommentAddGroup");
        public static string TableCommentDeleteGroup => Get("TableCommentDeleteGroup");
        public static string TableCommentGroupName => Get("TableCommentGroupName");
        public static string TableCommentMembers => Get("TableCommentMembers");
        public static string TableCommentAddPlaceholder => Get("TableCommentAddPlaceholder");
        public static string TableAlignLeft => Get("TableAlignLeft");
        public static string TableAlignCenter => Get("TableAlignCenter");
        public static string TableAlignRight => Get("TableAlignRight");
        public static string TableAlignColumn => Get("TableAlignColumn");
        public static string ExcelImport => Get("ExcelImport");
        public static string ExcelImportTip => Get("ExcelImportTip");
        public static string ExcelImportTitle => Get("ExcelImportTitle");
        public static string ExcelImportSheet => Get("ExcelImportSheet");
        public static string ExcelImportHasHeaderRow => Get("ExcelImportHasHeaderRow");
        public static string ExcelImportMappingHint => Get("ExcelImportMappingHint");
        public static string ExcelImportNotMapped => Get("ExcelImportNotMapped");
        public static string ExcelImportFilter => Get("ExcelImportFilter");
        public static string ExcelImportFileMissing => Get("ExcelImportFileMissing");
        public static string ExcelImportSheetEmpty => Get("ExcelImportSheetEmpty");
        public static string ExcelImportNoReport => Get("ExcelImportNoReport");
        public static string ExcelImportNoRows => Get("ExcelImportNoRows");
        public static string ExcelImportWorking => Get("ExcelImportWorking");
        /// <summary>Подпись кнопки добавления снимка в таблице: вид (Revit) или картинка (Win).</summary>
        public static string TableRowDeleteIssue => Get("TableRowDeleteIssue");
        public static string TableRowNumberHeader => Get("TableRowNumberHeader");
        public static string TableAddSnapshotLink => Get("TableAddSnapshotLink");
        public static string TableAddIssueLink => Get("TableAddIssueLink");
        public static string TableOpenSnapshot => Get("TableOpenSnapshot");
        public static string TableDocument => Get("TableDocument");
        public static string TableDocumentTip => Get("TableDocumentTip");
        public static string DocumentWindowTitle => Get("DocumentWindowTitle");
        public static string DocumentHint => Get("DocumentHint");
        public static string DocumentTitleLabel => Get("DocumentTitleLabel");
        public static string DocumentTitleHint => Get("DocumentTitleHint");
        public static string DocumentSubtitleLabel => Get("DocumentSubtitleLabel");
        public static string DocumentSubtitleHint => Get("DocumentSubtitleHint");
        public static string DocumentShowFieldBlocks => Get("DocumentShowFieldBlocks");
        public static string DocumentShowRowNumbers => Get("DocumentShowRowNumbers");
        public static string DocumentFieldsTitle => Get("DocumentFieldsTitle");
        public static string DocumentFieldsHint => Get("DocumentFieldsHint");
        public static string DocumentFieldName => Get("DocumentFieldName");
        public static string DocumentFieldValue => Get("DocumentFieldValue");
        public static string DocumentFieldSection => Get("DocumentFieldSection");
        public static string DocumentNoFields => Get("DocumentNoFields");

        public static string TableAddSnapshot =>
          Data.BcfHostCapabilities.SupportsOpenViewInModel ? Get("AddView") : Get("LoadLocalImage");
    }
}
