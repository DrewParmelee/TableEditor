using System.Windows;

namespace AOTableEditor;

public partial class XmlFormatReferenceWindow : Window
{
    public XmlFormatReferenceWindow()
    {
        InitializeComponent();
        ReferenceText.Text = ReferenceContent;
        ReferenceText.CaretIndex = 0;
    }

    private const string ReferenceContent =
@"AO Table Editor XML Format Reference

Root
----
The document root must be <tables>. Only direct child <table> elements are loaded as editable tables.

Root metadata is stored as attributes on <tables>:

  <tables
      lineOfBusiness=""BOP""
      state=""MI""
      newBusinessEffectiveDate=""2026-01-01""
      renewalEffectiveDate=""2026-12-01"">

Metadata attributes:
- lineOfBusiness: optional. Supported editor choices are BOP, TPP, CPP.
- state: optional. US state/DC abbreviation.
- newBusinessEffectiveDate: optional. Stored as yyyy-MM-dd.
- renewalEffectiveDate: optional. Stored as yyyy-MM-dd.

Table
-----
Each table is a direct child of <tables>:

  <table name=""Example"" delimiter="","" dataType=""float"" decimals=""3"" comment=""Optional note"">
    ...
  </table>

Table attributes:
- name: required. The table label shown on the left.
- delimiter: optional. Defaults to comma. Used to split and write each <data><row> value list.
- comment: optional. Displayed as read-only text above the table; editable in Table Properties.
- dataType: optional. Supported values are string, int, float, double, decimal. Missing/blank means string.
- decimals: optional. Applies only to float, double, and decimal. Cell edits are formatted with trailing zeroes.

Numeric validation:
- int requires whole-number values.
- float, double, and decimal require numeric values.
- A single dash (-) is accepted for numeric tables as a loud-error/rating marker.
- Blank cells are accepted.
- If decimals=""4"", entering 4.4 stores 4.4000.

Comments
--------
The editor reads either:

  <table comment=""Text here"">

or the older child form:

  <comment>Text here</comment>

When Table Properties updates a comment, the editor writes the comment attribute and removes the child <comment> element.

Row Keys
--------
Rows are defined by <rowKeys> containing one or more <rowSet> elements:

  <rowKeys>
    <rowSet name=""CoverageType"" searchType=""eq"">
      <key>InstallationFloater</key>
      <key>RehabAndReno</key>
    </rowSet>
    <rowSet name=""CoverageDetail"">
      <key>SewerBackup</key>
      <key>RentalIncome</key>
    </rowSet>
  </rowKeys>

rowSet attributes:
- name: required.
- searchType: optional metadata for other applications. The editor preserves/edits it but does not execute search logic.

Column Keys
-----------
Columns are defined by <colKeys> containing one or more <colSet> elements:

  <colKeys>
    <colSet name=""PerilType"" searchType=""eq"">
      <key>BG1</key>
      <key>BG2</key>
    </colSet>
  </colKeys>

colSet has the same name/searchType behavior as rowSet.

Pages
-----
Pages are optional and are defined by <pageKeys>:

  <pageKeys searchType=""eq"">
    <key>Base</key>
    <key>Renewal</key>
  </pageKeys>

pageKeys attributes:
- searchType: optional metadata for other applications.

Rendering:
- If there is zero or one page, the page tab row is hidden.
- If there are multiple pages, each page appears as a small tab above the table.
- Data is page-major: all data rows for page 1, then all rows for page 2, and so on.

Search Types
------------
The editor can store these values on rowSet, colSet, and pageKeys:

  eq, lt, lte, gt, gte, range, interpolate, graduated

In the UI these appear as:

  =, <, <=, >, >=, Range, Interpolate, Graduated

The editor does not perform search/rating lookup logic. These values are metadata for consuming applications.

Data
----
Data is stored under <data> as one <row> element per row-key combination for each page:

  <data>
    <row>1.000,2.000</row>
    <row>3.000,4.000</row>
  </data>

The text inside each <row> is split by the table delimiter. Each value maps to a column-key combination.

Combination Order
-----------------
Row, column, and page values are ordered by the XML order of their keys.

For row sets:
- The first rowSet is the outer grouping.
- Later rowSets vary inside earlier rowSets.
- In the grid, repeated row-header labels are blanked for readability except the deepest row key.

For column sets:
- The first colSet is the outer grouping.
- Later colSets vary inside earlier colSets.
- In the grid, repeated column-header labels are blanked for readability except the deepest column key.

For pages:
- If pageKeys are present, each page owns a full block of data rows.
- If pageKeys are absent, the table behaves as one implicit default page.

Data row index formula:

  source row index = (page index * row combination count) + row combination index

Data value lookup:

  data[source row index][column combination index]

Missing data rows or missing values render as blank cells.

Rendering Surface
-----------------
The grid is built from:
- Column header rows: one row per colSet.
- Row-set header row: one row containing rowSet names.
- Data rows: one row per row-key combination for the selected page.

Row header columns:
- One left-side column per rowSet.
- These are read-only in the main table view.

Value columns:
- One value column per column-key combination.
- These are editable in the pending view.

Current vs Pending
------------------
The editor keeps two versions while a file is open:
- Current: the XML loaded from disk.
- Pending: a ghost XML document containing unsaved edits.

If a table differs between Current and Pending:
- The table label on the left shows an asterisk.
- Current/Pending view tabs appear.
- Changed cells are highlighted.

Save writes the pending ghost XML over the original file.

Export Format
-------------
Tools > Export Table writes CSV in long form.

The exported columns are:
- Page, when the table has explicit pageKeys.
- One column per rowSet, in XML order.
- One column per colSet, in XML order.
- Value.

Each exported CSV row represents one page + row-key combination + column-key combination.

Example export columns:

  Page,CoverageType,CoverageDetail,PerilType,Value

This long-form export intentionally treats pages as a top-level column rather than separate worksheets or separate files.";
}
