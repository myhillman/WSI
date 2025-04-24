Imports System.ComponentModel
Imports System.Drawing.Printing
Imports System.Net.Http
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.AspNetCore.WebUtilities
Imports MySql.Data.MySqlClient

''' <summary>
''' Represents the main form of the application, providing a user interface for managing and displaying 
''' amateur radio QSO (contact) data and interacting with a DX cluster.
''' </summary>
''' <remarks>
''' The <see cref="Form1"/> class includes the following key features:
''' - **Tab Management**: Dynamically creates and manages tabs for different callsigns, each containing 
'''   DataGridView controls to display QSO data.
''' - **Database Integration**: Connects to an HRD logbook database to retrieve and display QSO data.
''' - **DX Cluster Integration**: Provides functionality to interact with a DX cluster through a separate 
'''   cluster management form.
''' - **Customizable UI**: Allows users to save and restore window positions and manage startup settings.
''' - **Printing Support**: Includes functionality to print the content of the active tab.
''' - **Event Handling**: Handles user interactions such as key presses, button clicks, and tab selection changes.
''' 
''' This form serves as the main entry point for the application, enabling users to manage and visualize 
''' amateur radio data in a user-friendly interface.
''' </remarks>
Public Class Form1
    ' Dictionary to store the saved locations of windows from the last session
    Public Shared frmClusterInstance As frmCluster      ' global instance
    Public con As New MySqlConnection(ConnectString)     ' connect to HRD log

    ''' <summary>
    ''' Creates a new tab in TabControl1 for the specified callsign.
    ''' </summary>
    ''' <param name="callsign">The callsign to create a tab for.</param>
    ''' <remarks>
    ''' This method checks if a tab for the given callsign already exists. If it does, 
    ''' the method selects the existing tab. Otherwise, it creates a new tab with a 
    ''' TableLayoutPanel containing labels and DataGridView controls for displaying 
    ''' QSO data related to the callsign.
    ''' 
    ''' Key features:
    ''' - Dynamically creates a new TabPage for the callsign.
    ''' - Adds a TableLayoutPanel to organize controls within the tab.
    ''' - Populates the tab with labels and DataGridView controls.
    ''' - Switches to the newly created or existing tab.
    ''' </remarks>
    Private Async Sub CreateTabForCallsign(callsign As String)
        ' Check if a tab for this callsign already exists
        For Each tab As TabPage In TabControl1.TabPages
            If tab.Text = callsign Then
                TabControl1.SelectedTab = tab ' Switch to the existing tab
                Return
            End If
        Next

        ' Create a new TabPage
        Dim newTab As New TabPage(callsign) With {
            .Name = $"Tab_{callsign}",
            .Tag = callsign ' Store the callsign for reference
            }

        ' Add controls to the TabPage (e.g., DataGridView, labels)
        With newTab
            Dim tpl = New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .Name = $"TableLayoutPanel_{callsign}",
                .RowCount = 4,
                .ColumnCount = 1
            }
            .Controls.Add(tpl) ' Add the TableLayoutPanel to the TabPage
            tpl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) ' Set column style
            With tpl
                Dim label1 = New Label With {
                    .Dock = DockStyle.Top,
                    .AutoSize = True,
                    .Name = $"Label1_{callsign}"
                }
                tpl.SetCellPosition(label1, New TableLayoutPanelCellPosition(0, 0))
                .Controls.Add(label1)

                Dim dgv1 = New DataGridView With {
                .Name = $"DataGridView1_{callsign}",
                .Dock = DockStyle.Fill,
                .AllowUserToAddRows = False
            }
                tpl.SetCellPosition(dgv1, New TableLayoutPanelCellPosition(1, 0))
                .Controls.Add(dgv1)

                Dim label2 = New Label With {
                .Dock = DockStyle.Top,
                .AutoSize = True,
                .Name = $"Label2_{callsign}"
            }
                tpl.SetCellPosition(label2, New TableLayoutPanelCellPosition(2, 0))
                .Controls.Add(label2)

                Dim dgv2 = New DataGridView With {
                .Name = $"DataGridView2_{callsign}",
                .Dock = DockStyle.Fill,
                .AllowUserToAddRows = False
            }
                tpl.SetCellPosition(dgv2, New TableLayoutPanelCellPosition(3, 0))
                .Controls.Add(dgv2)
            End With
        End With

        ' Add the TabPage to TabControl1
        TabControl1.TabPages.Add(newTab)
        TabControl1.SelectedTab = newTab

        ' Populate the DataGridView
        Await PopulateMatrixForTab(newTab)
    End Sub
    Private Async Function PopulateMatrixForTab(tab As TabPage) As Task
        Const Phone = "COL_MODE IN ('AM','SSB','USB','LSB','FM')"       ' SQL fragment for Phone mode
        Const CW = "COL_MODE ='CW'"                ' SQL fragment for CW mode
        Const Digital = "COL_MODE IN ('AMTOR','ARDOP','CHIP','CLOVER','CONTESTI','DOMINO','DSTAR','FREEDV','FSK31','FSK441','FT4','FT8','GTOR','HELL','HFSK','ISCAT','JT4','JT65','JT6M','JT9','MFSK','MINIRTTY','MSK144','MT63','OLIVIA','OPERA','PACKET','PACTOR','PAX','PSK10','PSK125','PSK2K','PSK31','PSK63','PSK63F','PSKAM','PSKFEC31','Q15','QRA64','ROS','RTTY','RTTYM','SSTV','T10','THOR','THROB','VOI','WINMOR')"    ' SQL fragment for Digital mode
        Const Confirmed = "SUM(IF(COL_QSL_RCVD='Y' OR COL_EQSL_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='V',1,0))"
        Dim callsign = tab.Tag.ToString()       ' callsign of tab
        Dim tpl = tab.Controls.Find($"TableLayoutPanel_{callsign}", True).FirstOrDefault()
        Dim sqldr As MySqlDataReader, DXCC As Integer = 0, Country As String = "", result As Integer = 1
        Dim clublog As New List(Of (band As String, mode As String)), band As String = "", mode As String = ""
        Dim ClublogJSON As New Dictionary(Of String, Dictionary(Of String, Integer))

        ' Create DataTables for DataGridView1 and DataGridView2
        Dim dt1 As New DataTable()
        Dim dt2 As New DataTable()

        ' Initialize DataTables with columns for Band and Mode
        dt1.Columns.Add("Mode", GetType(String)) ' Row headers
        dt2.Columns.Add("Mode", GetType(String)) ' Row headers
        ' Add a band column for each band that is enabled for cluster display
        Dim savedBands As List(Of String) = My.Settings.AmateurBands.Split(","c).Select(Function(b) b.Trim()).ToList()
        For Each band In savedBands
            dt1.Columns.Add($"BAND_{band}", GetType(String))
            dt2.Columns.Add($"BAND_{band}", GetType(String))
        Next

        ' Get clublog matches

        Using httpClient As New HttpClient()
            Try
                Dim GETfields As New Dictionary(Of String, String) From {
                            {"api", CLUBLOG_API_KEY},
                            {"call", "vk3ohm"},
                            {"log", callsign}
                    }
                httpClient.Timeout = New TimeSpan(0, 5, 0)        ' 5 min timeout
                httpClient.DefaultRequestHeaders.Clear()
                Try
                    ' Get JSON from clublog, and decode
                    Dim url As New Uri(QueryHelpers.AddQueryString("https://clublog.org/logsearchjson.php", GETfields))
                    ClublogJSON = Await httpClient.GetFromJsonAsync(url, ClublogJSON.GetType)
                    ' valid json
                    For Each bnd In ClublogJSON
                        band = $"{bnd.Key}m"
                        For Each md In bnd.Value
                            Select Case md.Key
                                Case "P" : mode = "Phone"
                                Case "D" : mode = "Digital"
                                Case "C" : mode = "CW"
                            End Select
                            clublog.Add((band, mode))
                        Next
                    Next
                Catch ex As Exception
                    ' Just ignore if error
                End Try
            Catch ex As HttpRequestException
                MsgBox(ex.Message & vbCrLf & ex.StackTrace, vbCritical + vbOKOnly, "Exception")
            End Try
        End Using

        ' Populate DataTable for DataGridView1
        Using cmd As New MySqlCommand()
            With cmd
                .Connection = con
                .CommandText = $"SELECT COL_DXCC AS DXCC, COL_FREQ_RX, COL_COUNTRY as Country, COL_BAND as BAND,
                                    CASE WHEN {Phone} THEN 'Phone'
                                    WHEN {CW} THEN 'CW'
                                    WHEN {Digital} THEN 'Digital'
                                    ELSE 'Other' END as MODE,
                                    {Confirmed} AS Confirmed
                                    FROM TABLE_HRD_CONTACTS_V01
                                    WHERE COL_CALL='{callsign}'
                                    GROUP BY BAND,MODE
                                    ORDER BY COL_FREQ DESC"       ' get records of this callsign
                .CommandType = CommandType.Text
            End With
            Dim lbl1 = tab.Controls.Find($"Label1_{callsign}", True).FirstOrDefault()
            sqldr = cmd.ExecuteReader
            Dim PartialLabel = $"QSO for {callsign}"
            If Not sqldr.HasRows Then
                ' No callsign in database
                lbl1.Text = $"No {PartialLabel}"
            Else
                lbl1.Text = PartialLabel
                While sqldr.Read
                    ' create column (band) if does not exist
                    'Dim col As Integer = -1, row As Integer = -1
                    band = sqldr("BAND")
                    mode = sqldr("MODE")
                    Dim ColumnName = $"BAND_{band}"

                    ' Add column if it doesn't exist
                    If Not dt1.Columns.Contains(ColumnName) Then
                        dt1.Columns.Add(ColumnName, GetType(String))
                    End If

                    ' Add row if it doesn't exist
                    Dim row = dt1.Rows.Cast(Of DataRow).FirstOrDefault(Function(r) r("Mode").ToString() = mode)
                    If row Is Nothing Then
                        row = dt1.NewRow()
                        row("Mode") = mode
                        dt1.Rows.Add(row)
                    End If

                    DXCC = sqldr("DXCC")
                    Country = sqldr("Country")

                    ' Set cell value and style
                    Dim QSL As String = "W"     ' worked
                    If sqldr("Confirmed") > 0 Then
                        QSL = "C"    ' confirmed
                    ElseIf clublog.Any(Function(t) t.band = band AndAlso t.mode = mode) Then
                        QSL = "CL" ' Clublog match
                    End If
                    row(ColumnName) = QSL
                End While
            End If
            sqldr.Close()
        End Using

        ' Bind DataTable to DataGridView1
        Dim dgv1 As DataGridView = CType(tab.Controls.Find($"DataGridView1_{callsign}", True).FirstOrDefault(), DataGridView)
        dgv1.DataSource = dt1
        FormatDataGridView(dgv1)

        ' Populate DataTable for DataGridView2
        If DXCC = 0 Then
            ' no DXCC available. Get DXCC from clublog
            Using httpClient As New HttpClient()
                Try
                    Dim GETfields As New Dictionary(Of String, String) From {
                            {"call", callsign},
                            {"api", CLUBLOG_API_KEY},
                            {"full", 1}
                    }
                    httpClient.Timeout = New TimeSpan(0, 5, 0)        ' 5 min timeout
                    httpClient.DefaultRequestHeaders.Clear()
                    Dim url As New Uri(QueryHelpers.AddQueryString("https://clublog.org/dxcc", GETfields))
                    Dim httpResult As HttpResponseMessage = Await httpClient.GetAsync(url)
                    httpResult.EnsureSuccessStatusCode()
                    Dim response = Await httpResult.Content.ReadAsStringAsync()
                    Dim cl As JsonDocument = JsonDocument.Parse(response)
                    DXCC = cl.RootElement.GetProperty("DXCC").GetUInt32
                    Country = cl.RootElement.GetProperty("Name").GetString
                Catch ex As HttpRequestException
                    MsgBox(ex.Message & vbCrLf & ex.StackTrace, vbCritical + vbOKOnly, "Exception")
                End Try
            End Using
        End If

        Using cmd As New MySqlCommand()
            With cmd
                .Connection = con
                .CommandText = $"SELECT COL_DXCC AS DXCC, COL_BAND as BAND,
                                            CASE WHEN {Phone} THEN 'Phone'
                                            WHEN {CW} THEN 'CW'
                                            WHEN {Digital} THEN 'Digital'
                                            ELSE 'Other' END as MODE,
                                            {Confirmed} AS Confirmed
                                            FROM TABLE_HRD_CONTACTS_V01
                                            WHERE COL_DXCC='{DXCC}'
                                            GROUP BY BAND,MODE
                                            ORDER BY COL_FREQ DESC"       ' get records of this callsign
                .CommandType = CommandType.Text
            End With
            Dim lbl2 = tab.Controls.Find($"Label2_{callsign}", True).FirstOrDefault()
            sqldr = cmd.ExecuteReader
            Dim PartialLabel = $"QSO for {Country} ({DXCC})"
            If Not sqldr.HasRows Then
                ' No callsign in database
                lbl2.Text = $"No {PartialLabel}"
            Else
                lbl2.Text = PartialLabel
                While sqldr.Read
                    ' create column (band) if does not exist
                    band = sqldr("BAND")
                    mode = sqldr("MODE")
                    DXCC = sqldr("DXCC")
                    Dim ColumnName = $"BAND_{band}"

                    ' Add column if it doesn't exist
                    If Not dt2.Columns.Contains(ColumnName) Then
                        dt2.Columns.Add(ColumnName, GetType(String))
                    End If

                    ' Add row if it doesn't exist
                    Dim row = dt2.Rows.Cast(Of DataRow).FirstOrDefault(Function(r) r("Mode").ToString() = mode)
                    If row Is Nothing Then
                        row = dt2.NewRow()
                        row("Mode") = mode
                        dt2.Rows.Add(row)
                    End If

                    ' Set cell value and style
                    Dim QSL As String = "W"     ' worked
                    If sqldr("Confirmed") > 0 Then
                        QSL = "C"    ' confirmed
                    End If
                    row(ColumnName) = QSL
                End While
            End If
            sqldr.Close()
        End Using

        ' Bind DataTable to DataGridView2
        Dim dgv2 As DataGridView = CType(tab.Controls.Find($"DataGridView2_{callsign}", True).FirstOrDefault(), DataGridView)
        dgv2.DataSource = dt2
        FormatDataGridView(dgv2)

        ' Add handlers for cell formatting
        AddHandler dgv1.CellFormatting, AddressOf DGVCellFormatting
        AddHandler dgv2.CellFormatting, AddressOf DGVCellFormatting

        ' resize the form
        tpl.PerformLayout()       ' let panel size itself
        Me.ClientSize = tpl.PreferredSize
        Dim borderWidth As Integer = Me.Width - Me.ClientSize.Width
        Dim titleBarHeight As Integer = Me.Height - Me.ClientSize.Height
        Me.Size = New Size(tpl.PreferredSize.Width + borderWidth, tpl.PreferredSize.Height + titleBarHeight)

        ' Force layout updates
        Me.PerformLayout()
        Me.Refresh()
        Return
    End Function

    ''' <summary>
    ''' Applies consistent formatting to a specified DataGridView control.
    ''' This method customizes the appearance and behavior of the DataGridView, 
    ''' including header styles, grid colors, row and column sizing, and alignment.
    ''' </summary>
    ''' <param name="dgv">The DataGridView control to format.</param>
    ''' <remarks>
    ''' Key formatting applied by this method:
    ''' 1. **Header Styles**:
    '''    - Sets custom background and foreground colors for column and row headers.
    '''    - Aligns header text to the center.
    ''' 2. **Grid and Background Colors**:
    '''    - Sets the grid color to white and the background color to light gray.
    ''' 3. **Column Behavior**:
    '''    - Disables sorting for columns by setting the `SortMode` to `Automatic`.
    ''' 4. **Row and Column Sizing**:
    '''    - Automatically adjusts row heights and column widths to fit their content.
    '''    - Adjusts the overall DataGridView size to fit its content.
    ''' 5. **General Appearance**:
    '''    - Disables visual styles for headers to allow custom styling.
    '''    - Ensures row headers are sized to fit their content.
    ''' 
    ''' This method ensures a consistent and user-friendly appearance for DataGridView controls
    ''' used in the application.
    ''' </remarks>
    Private Sub FormatDataGridView(dgv As DataGridView)
        With dgv
            .SuspendLayout()
            .RowHeadersVisible = False      ' hide the Select column
            .EnableHeadersVisualStyles = False
            .BackgroundColor = Color.LightGray

            ' Set autosizing properties
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells

            ' Set header styles
            With .ColumnHeadersDefaultCellStyle
                .BackColor = Color.LightBlue
                .ForeColor = Color.Blue
                .Alignment = DataGridViewContentAlignment.MiddleCenter
            End With

            ' Center all cells in the DataGridView
            For Each col As DataGridViewColumn In .Columns
                With col
                    .DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter ' Center cell content
                    .SortMode = DataGridViewColumnSortMode.NotSortable
                    .HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                End With
            Next

            With .Columns("Mode").DefaultCellStyle
                .BackColor = Color.LightBlue
                .ForeColor = Color.Blue
            End With

            ' Set custom column headers
            For Each col As DataGridViewColumn In .Columns
                If col.Name.StartsWith("BAND_") Then
                    col.HeaderText = col.Name.Replace("BAND_", "") ' Example: Change "BAND_20m" to "20m"
                End If
            Next
            .RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders
            .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            .ScrollBars = ScrollBars.None
            .Width = .Columns.GetColumnsWidth(DataGridViewElementStates.Visible) + .RowHeadersWidth + 2
            .Height = .Rows.GetRowsHeight(DataGridViewElementStates.Visible) + .ColumnHeadersHeight + 4
            .ResumeLayout()
        End With
    End Sub
    ''' <summary>
    ''' Handles the formatting of DataGridView cells based on their content.
    ''' This method dynamically changes the background and foreground colors of cells
    ''' in the DataGridView based on the cell's value.
    ''' </summary>
    ''' <param name="sender">The DataGridView that triggered the event.</param>
    ''' <param name="e">Provides data for the CellFormatting event.</param>
    Sub DGVCellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        ' Ensure the cell is not a header cell
        If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 Then
            ' Get the cell value

            If e.ColumnIndex = 0 Then
                ' it's the mode column
                e.CellStyle.BackColor = Color.LightBlue
                e.CellStyle.ForeColor = Color.Blue
            Else
                Dim cellValue As String = If(e.Value IsNot Nothing, e.Value.ToString(), String.Empty)
                ' Determine the background color based on the cell value
                Select Case cellValue
                    Case "C" ' Confirmed
                        e.CellStyle.BackColor = Color.Green
                    Case "W" ' Worked
                        e.CellStyle.BackColor = Color.LightGray
                    Case "CL" ' Clublog match
                        e.CellStyle.BackColor = Color.Yellow
                    Case Else
                        e.CellStyle.BackColor = Color.White ' Default color
                End Select
                e.CellStyle.ForeColor = Color.Black
            End If
        End If
    End Sub
    ''' <summary>
    ''' Handles the Click event for Button3 to print the current form.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically Button3.</param>
    ''' <param name="e">Provides data for the Click event.</param>
    ''' <remarks>
    ''' This method performs the following steps:
    ''' 1. Configures the print settings, including setting the page orientation to landscape and margins to zero.
    ''' 2. Attaches the PrintPage event handler to the PrintDocument object.
    ''' 3. Initiates the printing process for the current form.
    ''' 4. Removes the PrintPage event handler after printing is complete.
    ''' 
    ''' If an error occurs during the printing process, an error message is displayed to the user.
    ''' </remarks>

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        Try
            With PrintDocument1
                ' Set the page orientation to landscape
                With .DefaultPageSettings
                    .Landscape = True
                    .Margins = New Margins(0, 0, 0, 0) ' Set margins to zero
                End With

                ' Attach the PrintPage event handler
                AddHandler .PrintPage, AddressOf PrintImage

                ' Print the document
                .Print()

                ' Remove the event handler after printing
                RemoveHandler .PrintPage, AddressOf PrintImage
            End With
        Catch ex As Exception
            MessageBox.Show($"An error occurred while printing: {ex.Message}", "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' Handles the printing of the currently active tab in TabControl1.
    ''' Captures the content of the active tab as an image and scales it to fit within the printable area.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically the <see cref="PrintDocument"/> object.</param>
    ''' <param name="e">Provides data for the <see cref="PrintPageEventArgs"/> event, including graphics and page settings.</param>
    Private Sub PrintImage(ByVal sender As Object, ByVal e As PrintPageEventArgs)
        ' Get the currently active tab
        Dim activeTab As TabPage = TabControl1.SelectedTab
        If activeTab Is Nothing Then
            MessageBox.Show("No active tab to print.", "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            e.HasMorePages = False
            Return
        End If

        ' Capture the content of the active tab as a bitmap
        Dim tabImage As New Bitmap(activeTab.Width, activeTab.Height)
        activeTab.DrawToBitmap(tabImage, New Rectangle(0, 0, activeTab.Width, activeTab.Height))

        ' Get the printer's non-printable area
        Dim hardMarginX As Single = e.PageSettings.HardMarginX
        Dim hardMarginY As Single = e.PageSettings.HardMarginY

        ' Calculate the scaling factor to fit the image within the printable area
        Dim scaleX As Single = e.PageSettings.PrintableArea.Width / tabImage.Width
        Dim scaleY As Single = e.PageSettings.PrintableArea.Height / tabImage.Height
        Dim scale As Single = Math.Min(scaleX, scaleY) ' Maintain aspect ratio

        ' Calculate the scaled dimensions
        Dim scaledWidth As Single = tabImage.Width * scale
        Dim scaledHeight As Single = tabImage.Height * scale

        ' Calculate the position to align the image to the top of the printable area
        Dim offsetX As Single = hardMarginX + (e.PageSettings.PrintableArea.Width - scaledWidth) / 2
        Dim offsetY As Single = hardMarginY ' Align to the top of the printable area

        ' Draw the image at the scaled size and adjusted position
        e.Graphics.DrawImage(tabImage, offsetX, offsetY, scaledWidth, scaledHeight)

        ' Indicate that there are no more pages to print
        e.HasMorePages = False
    End Sub

    ''' <summary>
    ''' Captures the entire form, including its borders and title bar, as a bitmap image.
    ''' </summary>
    ''' <returns>
    ''' A <see cref="Bitmap"/> object containing the visual representation of the form.
    ''' </returns>
    ''' <remarks>
    ''' This method uses the <see cref="Graphics.CopyFromScreen"/> function to capture the form's
    ''' appearance directly from the screen. The captured image includes all visible elements of the form,
    ''' such as borders, title bar, and client area.
    ''' 
    ''' The method creates a bitmap with the same dimensions as the form and copies the screen content
    ''' starting from the form's top-left corner (<see cref="Form.Location"/>) with the size of the form
    ''' (<see cref="Form.Size"/>).
    ''' 
    ''' This method is useful for scenarios where the entire form needs to be saved or printed as an image.
    ''' </remarks>
    Private Function CaptureForm() As Bitmap
        ' Get the bounds of the form, including borders and title bar
        Dim formBounds As Rectangle = Me.Bounds

        ' Adjust the bounds slightly inward to avoid capturing the desktop background
        ' I don't know why this is happening
        Const margin = 8    ' unexplained margin left, right and bottom of image
        With formBounds
            .X += margin : .Y += 1 : .Width -= margin * 2 : .Height -= margin
        End With

        ' Create a bitmap with the size of the adjusted form bounds
        Dim bmp As New Bitmap(formBounds.Width, formBounds.Height)

        ' Ensure the form is fully visible and in the foreground
        Me.Activate()
        Me.BringToFront()
        Me.Refresh()
        Thread.Sleep(100) ' Allow the form to render completely

        ' Capture the form from the screen
        Using g As Graphics = Graphics.FromImage(bmp)
            g.CopyFromScreen(formBounds.Location, Point.Empty, formBounds.Size)
        End Using

        Return bmp
    End Function

#Region "EventHandlers"

    ' Handles the Load event for Form1
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        con.Open()

        ' Enable custom drawing for TabControl1
        TabControl1.DrawMode = TabDrawMode.OwnerDrawFixed ' Enable custom drawing
        AddHandler TabControl1.DrawItem, AddressOf TabControl1_DrawItem

        ' Load and display the list of startup calls
        TextBox2.Text = My.Settings.OnStartup
        Dim calls = Split(Me.TextBox2.Text, ",") ' Split the list of calls
        For callsign = LBound(calls) To UBound(calls)
            CreateTabForCallsign(calls(callsign)) ' Create forms for each startup call
        Next

    End Sub

    ' Handles the KeyPress event for TextBox1
    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TextBox1.KeyPress
        ' If the Enter key is pressed and the text length is at least 3
        If e.KeyChar = Microsoft.VisualBasic.ChrW(Keys.Return) And Len(TextBox1.Text) >= 3 Then
            ' Check if a form with the same callsign is already open
            Dim myforms As FormCollection = Application.OpenForms
            For Each frm As Form In myforms
                If frm.Text = TextBox1.Text Then Exit Sub ' Exit if the form is already open
            Next
            ' Create a new form for the entered callsign
            CreateTabForCallsign(TextBox1.Text)
        End If
    End Sub

    ' Handles the KeyPress event for TextBox2
    Private Sub TextBox2_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TextBox2.KeyPress
        ' Allow only alphanumeric characters, forward slashes, commas, Enter, and Backspace
        Dim regexp = New Regex("[A-Z0-9\/,]")
        e.KeyChar = e.KeyChar.ToString().ToUpper() ' Convert to uppercase
        e.Handled = Not (regexp.IsMatch(e.KeyChar.ToString()) Or e.KeyChar = ChrW(Keys.Back) Or e.KeyChar = vbBack)
    End Sub

    ' Handles the Click event for Button1
    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        ' Create and show a new instance of the frmCluster form
        frmClusterInstance = New frmCluster
        frmClusterInstance.Show()
    End Sub

    ''' <summary>
    ''' Handles the KeyDown event for TextBox2. Performs actions when the Enter key is pressed.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically TextBox2.</param>
    ''' <param name="e">Provides data for the KeyDown event, including the key that was pressed.</param>
    ''' <remarks>
    ''' When the Enter key is pressed, this method:
    ''' - Saves the updated startup calls to application settings.
    ''' - Closes all currently open WSI forms.
    ''' - Reopens WSI forms for each callsign listed in TextBox2.
    ''' - Prevents the Enter key from adding a new line in the TextBox.
    ''' </remarks>
    Private Sub TextBox2_KeyDown(sender As Object, e As KeyEventArgs) Handles TextBox2.KeyDown
        ' Check if the Enter key (CR) is pressed
        If e.KeyCode = Keys.Enter Then
            ' Save the updated startup calls to settings
            My.Settings.OnStartup = TextBox2.Text
            My.Settings.Save()

            ' Close any tabs not in the startup list and remove them from the TabControl
            Dim callsigns = My.Settings.OnStartup.Split(",").ToList    ' Split the list of calls
            ' Iterate through all TabPages in TabControl1
            For Each tab As TabPage In Me.TabControl1.TabPages
                If Not callsigns.Contains(tab.Tag) Then
                    ' Remove the TabPage from TabControl1
                    TabControl1.TabPages.Remove(tab)
                End If
            Next

            ' Create tab for any new callsigns in the startup list
            For Each item In callsigns
                Dim found As Boolean = False
                ' Check if the callsign already exists in the TabControl
                For Each tab As TabPage In Me.TabControl1.TabPages
                    If tab.Tag = item Then
                        found = True
                        Exit For
                    End If
                Next
                ' If the callsign does not exist, create a new tab for it
                If Not found Then CreateTabForCallsign(item)
            Next
            ' Prevent the Enter key from adding a new line in the TextBox
            e.SuppressKeyPress = True
        End If
    End Sub
    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        ' Closes the currently selected tab in TabControl1
        ' Check if there is a selected tab
        If TabControl1.SelectedTab IsNot Nothing Then
            ' Remove the selected tab
            TabControl1.TabPages.Remove(TabControl1.SelectedTab)
        Else
            MessageBox.Show("No tab is currently selected.", "Close Tab", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Sub TabControl1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabControl1.SelectedIndexChanged

    End Sub

    Private Sub TabControl1_DrawItem(sender As Object, e As DrawItemEventArgs) Handles TabControl1.DrawItem
        Dim tabControl As TabControl = CType(sender, TabControl)

        ' Get the tab rectangle and text
        Dim tabRect As Rectangle = tabControl.GetTabRect(e.Index)
        Dim tabText As String = tabControl.TabPages(e.Index).Text

        ' Check if the tab is the selected tab
        If e.Index = tabControl.SelectedIndex Then
            ' Highlight the active tab (e.g., change background and text color)
            e.Graphics.FillRectangle(Brushes.LightBlue, tabRect) ' Background color
            TextRenderer.DrawText(e.Graphics, tabText, tabControl.Font, tabRect, Color.Black, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        Else
            ' Default style for inactive tabs
            e.Graphics.FillRectangle(SystemBrushes.Control, tabRect) ' Background color
            TextRenderer.DrawText(e.Graphics, tabText, tabControl.Font, tabRect, Color.Black, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End If
    End Sub
#End Region
End Class
