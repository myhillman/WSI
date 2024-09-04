Imports System.Net.Http
Imports MySql.Data.MySqlClient
Imports Microsoft.AspNetCore.WebUtilities
Imports System.Text.Json
Imports System.Net.Http.Json
Imports System.Drawing.Printing

Public Class WSI
    Const HRDLogbook = "VK3OHM"
    Public ConnectString As String = "server=localhost;user=root;database=mysql;port=3306;password=rubbish"    ' HRD database
    Public con As New MySqlConnection(ConnectString)     ' connect to HRD log
    Const Phone = "COL_MODE IN ('AM','SSB','USB','LSB','FM')"       ' SQL fragment for Phone mode
    Const CW = "COL_MODE ='CW'"                ' SQL fragment for CW mode
    Const Digital = "COL_MODE IN ('AMTOR','ARDOP','CHIP','CLOVER','CONTESTI','DOMINO','FSK31','FSK441','FT4','FT8','GTOR','HELL','HFSK','ISCAT','JT4','JT65','JT6M','JT9','MFSK','MINIRTTY','MSK144','MT63','OLIVIA','OPERA','PACKET','PACTOR','PAX','PSK10','PSK125','PSK2K','PSK31','PSK63','PSK63F','PSKAM','PSKFEC31','Q15','QRA64','ROS','RTTY','RTTYM','T10','THOR','THROB','VOI','WINMOR')"    ' SQL fragment for Digital mode
    Const Confirmed = "SUM(IF(COL_QSL_RCVD='Y' OR COL_EQSL_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='Y' OR COL_LOTW_QSL_RCVD='V',1,0))"
    Private Async Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        Dim value = Await PopulateMatrix()
    End Sub

    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.DialogResult = System.Windows.Forms.DialogResult.Cancel
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

        ' Remove any existing data
        Me.DataGridView1.Rows.Clear()
        Me.DataGridView1.Columns.Clear()
        Me.DataGridView2.Rows.Clear()
        Me.DataGridView2.Columns.Clear()
        ' Get clublog matches
        Using httpClient As New HttpClient()
            Try
                Dim GETfields As New Dictionary(Of String, String) From {
                            {"api", "10a1bf0032a132383740feaff29dd902687fb4ac"},
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
                    Dim col As Integer = -1, row As Integer = -1
                    band = sqldr("BAND")
                    Dim ColumnName = $"BAND_{band}"
                    DXCC = sqldr("DXCC")
                    Country = sqldr("Country")
                    Dim dgvc = DataGridView1.Columns(ColumnName)
                    If dgvc Is Nothing Then
                        col = DataGridView1.Columns.Add(ColumnName, band)   ' add the column
                    Else
                        col = DataGridView1.Columns.IndexOf(dgvc)           ' return column number
                    End If
                    ' create row (mode) if does not exist
                    mode = sqldr("MODE")
                    For Each r In DataGridView1.Rows
                        If r.HeaderCell.value = mode Then row = r.index
                    Next
                    If row = -1 Then
                        row = DataGridView1.Rows.Add()
                        DataGridView1.Rows(row).HeaderCell.Value = mode
                    End If
                    Dim QSL As String = "W"     ' worked
                    Dim BackColor = Color.LightGray
                    If sqldr("Confirmed") > 0 Then
                        QSL = "C"    ' confirmed
                        BackColor = Color.Green
                    End If
                    If QSL = "W" Then
                        ' See if there is a clublog match
                        Dim c = clublog.Find(Function(t) t.band = band And t.mode = mode)   ' find matching clublog entry
                        If Not c.Equals((Nothing, Nothing)) Then
                            ' match found - add to matrix
                            QSL = "CL"
                            BackColor = Color.Yellow
                        End If
                    End If
                    DataGridView1.Rows(row).Cells(col).Value = QSL      ' add value into cell
                    DataGridView1.Rows(row).Cells(col).Style.BackColor = BackColor      ' add color into cell
                End While
            End If
            sqldr.Close()
            ' resize the DGV
            With DataGridView1
                .EnableHeadersVisualStyles = False
                .GridColor = Color.White
                .BackgroundColor = Color.LightGray
                With .ColumnHeadersDefaultCellStyle
                    .BackColor = Color.LightBlue
                    .ForeColor = Color.Blue
                End With
                With .RowHeadersDefaultCellStyle
                    .BackColor = Color.LightBlue
                    .ForeColor = Color.Blue
                End With
                For Each col As DataGridViewColumn In .Columns
                    With col
                        .SortMode = DataGridViewColumnSortMode.NotSortable
                        .HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                    End With
                Next
                .RowHeadersDefaultCellStyle.Padding = New Padding(0)    ' hide black triangle
                .RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders
                .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
                .Width = .Columns.GetColumnsWidth(DataGridViewElementStates.None) + .RowHeadersWidth + 2
                .Height = .Rows.GetRowsHeight(DataGridViewElementStates.None) + .ColumnHeadersHeight + 2
            End With
        End Using

        ' Second do the WSI for the nominated DXCC
        If DXCC = 0 Then
            ' no DXCC available. Get DXCC from clublog
            Using httpClient As New HttpClient()
                Try
                    Dim GETfields As New Dictionary(Of String, String) From {
                            {"call", $"{Me.Text}"},
                            {"api", "10a1bf0032a132383740feaff29dd902687fb4ac"},
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
                Label2.Text = $"No QSO for {DXCC}"
            Else
                While sqldr.Read
                    ' create column (band) if does not exist
                    Dim col As Integer = -1, row As Integer = -1
                    band = sqldr("BAND")
                    Dim ColumnName = $"BAND_{band}"
                    DXCC = sqldr("DXCC")
                    Dim dgvc = DataGridView2.Columns(ColumnName)

                    If dgvc Is Nothing Then
                        col = DataGridView2.Columns.Add(ColumnName, band)   ' add the column
                    Else
                        col = DataGridView2.Columns.IndexOf(dgvc)           ' return column number
                    End If
                    ' create row (mode) if does not exist
                    mode = sqldr("MODE")
                    For Each r In DataGridView2.Rows
                        If r.HeaderCell.value = mode Then row = r.index
                    Next
                    If row = -1 Then
                        row = DataGridView2.Rows.Add()
                        DataGridView2.Rows(row).HeaderCell.Value = mode
                    End If
                    Dim QSL As String = "W"     ' worked
                    Dim BackColor = Color.LightGray
                    If sqldr("Confirmed") > 0 Then
                        QSL = "C"    ' confirmed
                        BackColor = Color.Green
                    End If
                    DataGridView2.Rows(row).Cells(col).Value = QSL      ' add value into cell
                    DataGridView2.Rows(row).Cells(col).Style.BackColor = BackColor      ' add color into cell
                End While

                Label2.Text = $"QSO for {Country} ({DXCC})"
            End If
            sqldr.Close()
            ' format the DGV. Copy all styling from DGV1
            With DataGridView2
                .EnableHeadersVisualStyles = False
                .GridColor = DataGridView1.GridColor
                With .ColumnHeadersDefaultCellStyle
                    .BackColor = DataGridView1.ColumnHeadersDefaultCellStyle.BackColor
                    .ForeColor = DataGridView1.ColumnHeadersDefaultCellStyle.ForeColor
                    .Font = Me.DataGridView1.ColumnHeadersDefaultCellStyle.Font
                    .Alignment = DataGridViewContentAlignment.MiddleCenter
                End With
                With .RowHeadersDefaultCellStyle
                    .BackColor = DataGridView1.RowHeadersDefaultCellStyle.BackColor
                    .ForeColor = DataGridView1.RowHeadersDefaultCellStyle.ForeColor
                    .Font = Me.DataGridView1.RowHeadersDefaultCellStyle.Font
                End With
                For Each col As DataGridViewColumn In .Columns
                    With col
                        .SortMode = DataGridViewColumnSortMode.NotSortable
                        .HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                    End With
                Next
                .RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders
                .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
                .Width = .Columns.GetColumnsWidth(DataGridViewElementStates.None) + .RowHeadersWidth + 2
                .Height = .Rows.GetRowsHeight(DataGridViewElementStates.None) + .ColumnHeadersHeight + 2
            End With
        End Using
        ' resize the form
        Dim borderWidth = Me.Width - Me.ClientSize.Width
        Me.MaximumSize = New Size(TableLayoutPanel2.Width + borderWidth * 2, TableLayoutPanel1.Bottom + borderWidth + 100)
        Return result
    End Function
    ' The selected cell displays with a highlight background. Don't want this, so clear selection
    Private Sub DataGridView1_SelectionChanged(sender As Object, e As EventArgs) Handles DataGridView1.SelectionChanged
        DataGridView1.ClearSelection()
    End Sub
    Private Sub DataGridView2_SelectionChanged(sender As Object, e As EventArgs) Handles DataGridView2.SelectionChanged
        DataGridView2.ClearSelection()
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Dim graph As Graphics = Nothing
        Try
            ' gets the upper left hand coordinate of the form
            Dim frmleft As System.Drawing.Point = Me.Bounds.Location

            Dim bmp As New Bitmap(Me.Bounds.Width, Me.Bounds.Height)

            'creates the grapgic
            graph = Graphics.FromImage(bmp)

            'Gets the x,y coordinates from the upper left start point
            Dim screenx As Integer = frmleft.X
            Dim screeny As Integer = frmleft.Y

            graph.CopyFromScreen(screenx, screeny, 0, 0, bmp.Size)

            ' Save the Screenshot to a file
            bmp.Save("temp.png")

            'Open File and load in MS Paint
            Dim filepath As String
            filepath = "temp.png"
            Try
                AddHandler PrintDocument1.PrintPage, AddressOf Me.PrintImage
                With PrintDocument1
                    .DefaultPageSettings.Landscape = True
                    .Print() ' this prints the graphic using a function you also have to include called PrintImage
                End With
            Catch ex As Exception ' this will give your user a little additional information if there is an error in the printing
                MsgBox(ex, MsgBoxStyle.Critical, "Error during Print")
            End Try

            bmp.Dispose()
            graph.Dispose()
        Catch ex As Exception
            MsgBox(ex.Message)
        End Try
    End Sub
    Private Sub PrintImage(ByVal sender As Object, ByVal ppea As PrintPageEventArgs)
        ' Instruct VB to draw your required file
        Dim i As Image = Image.FromFile("temp.png")
        Dim m As Rectangle = ppea.MarginBounds
        If i.Width / i.Height > m.Width / m.Height Then ' image Is wider
            m.Height = i.Height / i.Width * m.Width
        Else
            m.Width = i.Width / i.Height * m.Height
        End If
        ppea.Graphics.DrawImage(i, m)
        ' Tell VB there are no more pages to print
        ppea.HasMorePages = False
    End Sub
End Class
