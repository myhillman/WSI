Imports System.Net.Http
Imports MySql.Data.MySqlClient
Imports Microsoft.AspNetCore.WebUtilities
Imports System.Text.Json
Imports System.Net.Http.Json
Imports System.Drawing.Printing

Public Class WSI
    Const HRDLogbook = "VK3OHM"
    Public con As New MySqlConnection(ConnectString)     ' connect to HRD log
    Const Phone = "COL_MODE IN ('AM','SSB','USB','LSB','FM')"       ' SQL fragment for Phone mode
    Const CW = "COL_MODE ='CW'"                ' SQL fragment for CW mode
    Const Digital = "COL_MODE IN ('AMTOR','ARDOP','CHIP','CLOVER','CONTESTI','DOMINO','DSTAR','FREEDV','FSK31','FSK441','FT4','FT8','GTOR','HELL','HFSK','ISCAT','JT4','JT65','JT6M','JT9','MFSK','MINIRTTY','MSK144','MT63','OLIVIA','OPERA','PACKET','PACTOR','PAX','PSK10','PSK125','PSK2K','PSK31','PSK63','PSK63F','PSKAM','PSKFEC31','Q15','QRA64','ROS','RTTY','RTTYM','SSTV','T10','THOR','THROB','VOI','WINMOR')"    ' SQL fragment for Digital mode
    Const Confirmed = "SUM(IF(COL_QSL_RCVD='Y' OR COL_EQSL_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='V',1,0))"
    Private Async Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        Dim value = Await PopulateMatrix()
    End Sub

    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.DialogResult = System.Windows.Forms.DialogResult.Cancel
        Form1.TextBox1.Clear()
        Me.Close()
    End Sub

    Private Async Sub WSI_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Open the HRD database
        con.Open()      ' open SQL database
        ' First do the WSI for the nominated callsign
        Dim value = Await PopulateMatrix()
    End Sub

    Private Async Function PopulateMatrix() As Task(Of Integer)
        Dim result As Integer = 1
        Dim sqldr As MySqlDataReader, DXCC As Integer = 0, Country As String = ""
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
                            {"log", $"{Me.Text}"}
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
                                    WHERE COL_CALL='{Me.Text}'
                                    GROUP BY BAND,MODE
                                    ORDER BY COL_FREQ DESC"       ' get records of this callsign
                .CommandType = CommandType.Text
            End With
            sqldr = cmd.ExecuteReader
            If Not sqldr.HasRows Then
                ' No callsign in database
                Label1.Text = $"No QSO for {Me.Text}"
            Else
                Label1.Text = $"QSO for {Me.Text}"
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
        DataGridView1.DataSource = dt1
        FormatDataGridView(DataGridView1)

        ' Populate DataTable for DataGridView2
        If DXCC = 0 Then
            ' no DXCC available. Get DXCC from clublog
            Using httpClient As New HttpClient()
                Try
                    Dim GETfields As New Dictionary(Of String, String) From {
                            {"call", $"{Me.Text}"},
                            {"api", CLUBLOG_API_KEY},
                            {"full", 1}
                    }
                    httpClient.Timeout = New TimeSpan(0, 5, 0)        ' 5 min timeout
                    httpClient.DefaultRequestHeaders.Clear()
                    Dim url As New Uri(QueryHelpers.AddQueryString("https://clublog.org/dxcc", GETfields))
                    Dim httpResult = Await httpClient.GetAsync(url)
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
            sqldr = cmd.ExecuteReader
            If Not sqldr.HasRows Then
                ' No callsign in database
                Label2.Text = $"No QSO for {Me.Text}"
            Else
                Label2.Text = $"QSO for {Country} ({DXCC})"
                While sqldr.Read
                    ' create column (band) if does not exist
                    'Dim col As Integer = -1, row As Integer = -1
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
        DataGridView2.DataSource = dt2
        FormatDataGridView(DataGridView2)

        ' resize the form
        'Dim borderWidth = Me.Width - Me.ClientSize.Width
        'Me.MaximumSize = New Size(TableLayoutPanel2.Width + borderWidth * 2, TableLayoutPanel1.Bottom + borderWidth + 100)
        ' Calculate the required width for the form
        Dim requiredWidth As Integer = Math.Max(DataGridView1.Width + DataGridView1.Left + 20, DataGridView2.Width + DataGridView2.Left + 20) ' Add padding for borders and spacing

        ' Set the form's width to the required width
        Me.Width = Math.Max(requiredWidth, Me.MinimumSize.Width) ' Ensure it doesn't go below the minimum size
        ' Calculate the required height for the form
        Dim requiredHeight = TableLayoutPanel2.Height + TableLayoutPanel2.Top + 40
        Me.Height = Math.Max(requiredheight, Me.MinimumSize.Height)
        Return result
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
            .RowHeadersVisible = False      ' hide the Select column
            .EnableHeadersVisualStyles = False
            .GridColor = Color.White
            .BackgroundColor = Color.LightGray
            With .ColumnHeadersDefaultCellStyle
                .BackColor = Color.LightBlue
                .ForeColor = Color.Blue
            End With
            With .Columns("Mode").DefaultCellStyle
                .BackColor = Color.LightBlue
                .ForeColor = Color.Blue
            End With
            For Each col As DataGridViewColumn In .Columns
                With col
                    .SortMode = DataGridViewColumnSortMode.NotSortable
                    .HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                End With
            Next
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
            Me.ClientSize = TableLayoutPanel2.PreferredSize
            Dim borderWidth As Integer = Me.Width - Me.ClientSize.Width
            Dim titleBarHeight As Integer = Me.Height - Me.ClientSize.Height
            Me.Size = New Size(TableLayoutPanel2.PreferredSize.Width + borderWidth, TableLayoutPanel2.PreferredSize.Height + titleBarHeight)
        End With
    End Sub
    ' The selected cell displays with a highlight background. Don't want this, so clear selection
    Private Sub DataGridView1_SelectionChanged(sender As Object, e As EventArgs) Handles DataGridView1.SelectionChanged
        DataGridView1.ClearSelection()
    End Sub
    Private Sub DataGridView2_SelectionChanged(sender As Object, e As EventArgs) Handles DataGridView2.SelectionChanged
        DataGridView2.ClearSelection()
    End Sub
    Private Sub btnPrint_Click(sender As Object, e As EventArgs) Handles btnPrint.Click
        Try
            With PrintDocument1
                ' Set the page orientation to landscape
                .DefaultPageSettings.Landscape = True

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

        ' Create a bitmap with the size of the form bounds
        Dim bmp As New Bitmap(formBounds.Width, formBounds.Height)

        ' Capture the form from the screen
        Using g As Graphics = Graphics.FromImage(bmp)
            g.CopyFromScreen(formBounds.Location, Point.Empty, formBounds.Size)
        End Using

        Return bmp
    End Function

    ''' <summary>
    ''' Handles the printing of the form by capturing it as an image and scaling it to fit within the printable area.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically the <see cref="PrintDocument"/> object.</param>
    ''' <param name="e">Provides data for the <see cref="PrintPageEventArgs"/> event, including graphics and page settings.</param>
    ''' <remarks>
    ''' This method performs the following steps:
    ''' 1. Captures the entire form, including borders and title bar, as a bitmap using the <see cref="CaptureForm"/> method.
    ''' 2. Scales the captured image to fit within the printable area defined by <see cref="PrintPageEventArgs.MarginBounds"/>.
    '''    - Maintains the aspect ratio of the image to avoid distortion.
    ''' 3. Draws the scaled image onto the page using the <see cref="Graphics.DrawImage"/> method.
    ''' 4. Indicates that there are no additional pages to print by setting <see cref="PrintPageEventArgs.HasMorePages"/> to <c>False</c>.
    ''' 
    ''' This method is invoked during the <see cref="PrintDocument.PrintPage"/> event and is designed to handle a single-page print job.
    ''' </remarks>
    Private Sub PrintImage(ByVal sender As Object, ByVal e As PrintPageEventArgs)
        ' Capture the form as an image
        Dim formImage As Bitmap = CaptureForm()

        ' Get the aspect ratios of the image and the margin bounds
        Dim imageAspectRatio As Double = formImage.Width / formImage.Height
        Dim marginAspectRatio As Double = e.MarginBounds.Width / e.MarginBounds.Height

        ' Calculate the scaled dimensions
        Dim scaledWidth As Integer
        Dim scaledHeight As Integer
        If imageAspectRatio > marginAspectRatio Then
            ' Scale based on width
            scaledWidth = e.MarginBounds.Width
            scaledHeight = CInt(e.MarginBounds.Width / imageAspectRatio)
        Else
            ' Scale based on height
            scaledHeight = e.MarginBounds.Height
            scaledWidth = CInt(e.MarginBounds.Height * imageAspectRatio)
        End If

        ' Center the image within the margin bounds
        Dim offsetX As Integer = e.MarginBounds.X + (e.MarginBounds.Width - scaledWidth) \ 2
        Dim offsetY As Integer = e.MarginBounds.Y + (e.MarginBounds.Height - scaledHeight) \ 2

        ' Draw the image on the page
        e.Graphics.DrawImage(formImage, offsetX, offsetY, scaledWidth, scaledHeight)

        ' Indicate that there are no more pages to print
        e.HasMorePages = False
    End Sub

    Private Sub DataGridView1_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs) Handles DataGridView2.CellFormatting
        DGVCellFormatting(sender, e)
    End Sub
    Private Sub DataGridView2_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs) Handles DataGridView1.CellFormatting
        DGVCellFormatting(sender, e)
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

    Private Sub DataGridView1_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles DataGridView1.CellContentClick

    End Sub
End Class


