Imports System.Net.Sockets
Imports System.Net
Imports System.Threading
Imports System.ComponentModel
Imports System.Diagnostics.Metrics
Imports System.Security.Policy

Public Class frmCluster
    ' Constants for the DX cluster server and port
    Const DXcluster = "hrd.wa9pie.net"
    Const port = 8000

    ' TCP client for connecting to the DX cluster
    Dim Cluster As New TcpClient()
    Dim localAddr As IPAddress = IPAddress.Any
    Dim listener As New TcpListener(localAddr, port)

    ' BackgroundWorker for handling asynchronous communication with the cluster
    Dim WithEvents Bgw As New BackgroundWorker

    ' Flags to track the state of the connection and operations
    Private LoggedIn As Boolean = False
    Private ClusterBusy As Boolean = False
    Private GetSpots As Boolean = False
    Dim CallList As List(Of String)     ' collect on startup calls


    'Dim ClusterTable As New DataTable()        ' cluster data

    ' Enum to define actions for the BackgroundWorker
    Enum BGWaction
        TextBox   ' Update the TextBox with received data
        Send      ' Send a command to the cluster
        Logged    ' Mark the user as logged in
        Busy      ' Mark the cluster as not busy
    End Enum

    ' Handles the Load event for frmCluster
    Private Async Sub frmCluster_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Create data source

        With DataGridView1
            .AutoGenerateColumns = True
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            .DataSource = GetDataSource()   ' Bind the data source
            ' Hide uninteresting columns
            .Columns("CC11").Visible = False
            .Columns("Entity").Visible = False
            .Columns("Spotter DXCC").Visible = False
            .Columns("Spotter Node").Visible = False
            .Columns("ITU DX").Visible = False
            .Columns("CQ DX").Visible = False
            .Columns("ITU Spotter").Visible = False
            .Columns("CQ Spotter").Visible = False
            .Columns("Spotter State").Visible = False
            .Columns("DX State").Visible = False
            .Columns("Spotter Country").Visible = False
            .Columns("Spotter Grid").Visible = False
            DataGridView1.Sort(DataGridView1.Columns("Date"), ListSortDirection.Descending)
            DataGridView1.Sort(DataGridView1.Columns("Time"), ListSortDirection.Descending)
            .Refresh()
        End With

        ' Create the Age drop down

        BindTimeSpanComboBox(ComboBox1)
        ComboBox1.SelectedIndex = ComboBox1.DataSource.Rows.Count - 1 ' Set the selected index to the last item

        ' Connect to the DX cluster server
        Await Cluster.ConnectAsync(DXcluster, port)
        ' Ensure the connection is fully established
        Thread.Sleep(1000)      ' wait for connection
        If Not Cluster.Connected Then
            AppendTextSafe("Error: Unable to connect to the cluster server." & vbCrLf)
            Return
        End If
        ' Configure and start the BackgroundWorker
        With Bgw
            .WorkerSupportsCancellation = True
            .WorkerReportsProgress = True
            .RunWorkerAsync()
        End With

        ' Wait until the user is logged in
        While Not LoggedIn
            Application.DoEvents()
        End While

        ' Enable spot retrieval
        GetSpots = True
    End Sub

    ' Handles the DoWork event for the BackgroundWorker
    Private Sub Bgw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles Bgw.DoWork
        Static Dim buffer As String = "", cr As Integer
        Try
            While Not Bgw.CancellationPending ' Keep running until the worker is
                If Cluster.Client.Available > 0 Then
                    Dim byt_to_receive(Cluster.Available - 1) As Byte
                    Cluster.Client.Receive(byt_to_receive, 0, byt_to_receive.Length, SocketFlags.None)
                    Dim String_From_Byte As String = System.Text.Encoding.ASCII.GetString(byt_to_receive)
                    buffer += String_From_Byte

                    If Not LoggedIn Then
                        ' Look for Login: , password: without CR
                        If buffer.EndsWith("login: ") Then
                            AppendTextSafe(buffer)
                            SendCluster($"{ClusterUsername}{vbCrLf}") ' Send username
                        ElseIf buffer.EndsWith("password: ") Then
                            AppendTextSafe(buffer)
                            SendCluster($"{ClusterPassword}{vbCrLf}") ' Send password
                        ElseIf buffer.Contains(">>") Then
                            ' We are logged in
                            AppendTextSafe(buffer)
                            LoggedIn = True
                            SendCluster($"set/name Marc{vbCrLf}")
                            SendCluster($"set/qth Donvale{vbCrLf}")
                            SendCluster($"set/qra QF22oe{vbCrLf}")
                            SendCluster($"unset/echo{vbCrLf}")
                            SendCluster($"set/prompt >>{vbCrLf}")
                            SendCluster($"set/ve7cc{vbCrLf}")
                            SendCluster($"unset/wcy{vbCrLf}")
                            SendCluster($"unset/wwv{vbCrLf}")
                            SendCluster($"sh/filter{vbCrLf}")
                            SendCluster($"clear/spots all{vbCrLf}")
                            SendCluster($"sh/filter{vbCrLf}")
                        End If
                    Else
                        cr = buffer.IndexOf(vbCr)   ' Get the index of the first CR character
                        While cr <> -1
                            Dim message As String
                            If cr = 0 Then
                                message = ""
                            Else
                                message = buffer.Substring(0, cr) ' Extract the message
                            End If
                            buffer = buffer.Substring(cr + 2) ' Remove the processed message from the buffer
                            cr = buffer.IndexOf(vbCr) ' Check for the next message
                            ' Process the message
                            ProcessMessage(message)
                        End While
                    End If
                End If
                If LoggedIn Then
                    ' Poll the cluster for spots every 20 seconds
                    CallList = My.Settings.OnStartup.Split(","c).ToList    ' collect on startup calls
                    If Form1.SelectedCall IsNot Nothing Then CallList.Add(Form1.SelectedCall)                ' add current callsign
                    For Each callsign In CallList
                        ' Send the sh/dx command
                        SendCluster($"sh/dx {callsign}{vbCrLf}")

                        ' Wait for a response
                        Dim responseReceived As Boolean = False
                        Dim responseTimeout As Integer = 5000 ' 5 seconds timeout
                        Dim startTime As DateTime = DateTime.Now

                        While Not responseReceived AndAlso (DateTime.Now - startTime).TotalMilliseconds < responseTimeout
                            If Cluster.Client.Available > 0 Then
                                responseReceived = True
                                Exit While
                            End If
                            Thread.Sleep(100) ' Small delay to avoid busy-waiting
                        End While

                        If Not responseReceived Then
                            AppendTextSafe($"No response received for callsign: {callsign}{vbCrLf}")
                        End If
                        ' Process any remaining incoming data before the sleep
                        While Cluster.Client.Available > 0
                            Dim byt_to_receive(Cluster.Available - 1) As Byte
                            Cluster.Client.Receive(byt_to_receive, 0, byt_to_receive.Length, SocketFlags.None)
                            Dim String_From_Byte As String = System.Text.Encoding.ASCII.GetString(byt_to_receive)
                            buffer += String_From_Byte

                            ' Process the buffer for messages
                            cr = buffer.IndexOf(vbCr)
                            While cr <> -1
                                Dim message As String
                                If cr = 0 Then
                                    message = ""
                                Else
                                    message = buffer.Substring(0, cr) ' Extract the message
                                End If
                                buffer = buffer.Substring(cr + 2) ' Remove the processed message from the buffer
                                cr = buffer.IndexOf(vbCr) ' Check for the next message
                                ProcessMessage(message) ' Process the message
                            End While
                        End While

                        ' Wait for 20 seconds before sending the next command
                        Thread.Sleep(20000)
                    Next
                    ApplyAgeFilter(ComboBox1) ' Apply the age filter to the DataGridView
                End If
            End While
        Catch ex As Exception
            AppendTextSafe($"Error in DoWork: {ex.Message}{vbCrLf}")
        End Try
    End Sub
    Private Sub ProcessMessage(message As String)
        ' Log the message to TextBox1
        AppendTextSafe($"---> {message}{vbCrLf}")   ' display the message in the TextBox
        ' Handle cluster data (e.g., CC11 format)
        If message.Contains("^"c) Then
            ' Parse and display cluster data
            ParseClusterData(message)
        End If
    End Sub
    ''' <summary>
    ''' Parses and processes cluster data received from the DX cluster server.
    ''' Updates the DataGridView with new data if the received data is valid and not already present.
    ''' </summary>
    ''' <param name="data">The raw cluster data string received from the server.</param>
    ''' <remarks>
    ''' This method splits the incoming data string into columns using the "^" delimiter.
    ''' If the data represents a valid cluster message (e.g., starts with "CC11"), it checks whether
    ''' the data is already present in the DataTable bound to the DataGridView. If not, it adds the new data.
    ''' The method ensures thread safety by invoking updates on the UI thread if required.
    ''' </remarks>
    Private Sub ParseClusterData(data As String)
        If Me.DataGridView1.InvokeRequired Then
            Me.DataGridView1.Invoke(New Action(Of String)(AddressOf ParseClusterData), data)
        Else
            Dim columns As String() = data.Split("^"c) ' Split the string into columns
            If columns(0) = "CC11" Then     ' it's a cluster message
                Dim dt As DataTable = CType(DataGridView1.DataSource, DataTable) ' Get the DataTable
                ' Check if the DataTable already contains the data
                Dim rows As DataRow() = dt.Select($"Frequency='{columns(1)}' AND [DX Call]='{columns(2)}' AND Date='{columns(3)}' AND Time='{columns(4)}' AND Spotter='{columns(6)}'")
                If rows.Length = 0 Then     ' it does not, so add
                    Dim row As DataRow = dt.NewRow() ' Create a new DataRow
                    ' Update the DataGridView with new data
                    For i As Integer = 0 To columns.Length - 1 ' Loop through the columns
                        If i < dt.Columns.Count Then
                            row(i) = columns(i) ' Fill the DataRow with data
                        End If
                    Next
                    dt.Rows.Add(row) ' Add the new row to the DataTable
                End If

                DataGridView1.Refresh() ' Refresh the DataGridView to show the new data
            End If
        End If
    End Sub


    ''' <summary>
    ''' Sends a command or message to the DX cluster server over a TCP connection.
    ''' </summary>
    ''' <param name="msg">The message or command to send to the cluster server.</param>
    ''' <remarks>
    ''' This method converts the provided message into a byte array and sends it to the cluster server
    ''' using the established TCP connection. It also appends the message to the TextBox for logging purposes.
    ''' If an error occurs during the send operation, the error is logged to the TextBox.
    ''' </remarks>
    Sub SendCluster(ByVal msg As String)
        ' Wait until the cluster is not busy
        'While ClusterBusy
        '    Thread.Sleep(100)
        '    Application.DoEvents()
        'End While
        Try
            ' Append the command to the TextBox for display
            AppendTextSafe($"-> {msg}")

            ' Convert the command to bytes and send it to the cluster
            Dim byt_to_send() As Byte = System.Text.Encoding.ASCII.GetBytes($"{msg}")
            Cluster.Client.Send(byt_to_send, 0, byt_to_send.Length, SocketFlags.None)

            ' Mark the cluster as busy
            'ClusterBusy = True
            Thread.Sleep(500)
        Catch ex As Exception
            AppendTextSafe($"Error in SendCluster: {ex.Message}{vbCrLf}")
        End Try
    End Sub
    ' Handles the Closing event for frmCluster
    Private Sub frmCluster_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        ' Close the connection to the cluster
        Cluster.Close()
    End Sub
    ''' <summary>
    ''' Creates and returns a DataTable containing the structure and columns required for displaying DX cluster data.
    ''' This DataTable serves as the data source for the DataGridView in the frmCluster form.
    ''' </summary>
    ''' <returns>
    ''' A DataTable with predefined columns representing DX cluster data, such as frequency, call sign, date, time, and other metadata.
    ''' </returns>
    ''' <remarks>
    ''' The method defines the structure of the DataTable, including column names and data types.
    ''' It is used to initialize the DataGridView's data source and ensure consistency in the displayed data.
    ''' The columns include:
    ''' - "CC11": Represents the DX spot type.
    ''' - "Frequency": The frequency of the DX spot (Single).
    ''' - "DX Call": The call sign of the DX station.
    ''' - "Date": The date of the DX spot (Date).
    ''' - "Time": The time of the DX spot.
    ''' - Additional metadata columns such as "Comment", "Spotter", "Entity", and others.
    ''' </remarks>
    Private Function GetDataSource() As DataTable
        'Format for CC Cluster And DX Spider
        ' CC11 = DX Spot
        ' Frequency
        ' DX Call
        ' Date
        ' Time
        ' Comments
        ' Spotter
        ' DX country number (DX Spider only)
        ' Spotter country number (DX Spider only)
        ' Spotter Node
        ' ITU Zone of DX
        ' CQ Zone of DX
        ' ITU Zone of Spotter
        ' CQ Zone of Spotter
        ' DX State(correct state)
        ' Spotter State(correct state)
        ' DX Country(for DX Spider the state Is probably wrong if given)
        ' Spotter Country(for DX Spider the state Is probably wrong if given)
        ' DX Grid Square
        ' Spotter Grid Square
        ' Spotter's IP Address(CC Cluster only)

        Dim table As New DataTable(), ColumnHeadings() As String = {"CC11", "Frequency", "DX Call", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid"}

        ' Add column headings
        For i As Integer = 0 To ColumnHeadings.Length - 1
            table.Columns.Add(ColumnHeadings(i))
        Next
        table.Columns("Frequency").DataType = GetType(Single)
        table.Columns("Date").DataType = GetType(Date)

        Return table
    End Function
    ''' <summary>
    ''' Safely appends text to a TextBox control from any thread.
    ''' Ensures thread-safe updates to the TextBox by invoking the update on the UI thread if necessary.
    ''' </summary>
    ''' <param name="text">The text to append to the TextBox.</param>
    ''' <remarks>
    ''' This method is useful in scenarios where background threads (e.g., a BackgroundWorker) 
    ''' need to update the UI, which is not thread-safe by default.
    ''' It also limits the number of lines in the TextBox to a maximum of 50, 
    ''' removing older lines to maintain performance and readability.
    ''' </remarks>
    Private Sub AppendTextSafe(text As String)
        If Me.TextBox1.InvokeRequired Then
            Me.TextBox1.Invoke(New Action(Of String)(AddressOf AppendTextSafe), text)
        Else
            ' Append the new text
            Me.TextBox1.AppendText(text)

            Const maxLines As Integer = 50  ' Limit the number of lines to 50
            Dim lines As String() = Me.TextBox1.Lines
            If lines.Length > maxLines Then
                Me.TextBox1.Lines = lines.Skip(lines.Length - maxLines).ToArray()
            End If
            ' Scroll to the last line
            Me.TextBox1.SelectionStart = Me.TextBox1.Text.Length
            Me.TextBox1.ScrollToCaret()
        End If
    End Sub
    ''' <summary>
    ''' Creates and returns a DataTable containing predefined TimeSpan values and their descriptions.
    ''' This DataTable is used as the data source for a ComboBox to allow users to select a TimeSpan.
    ''' </summary>
    ''' <returns>
    ''' A DataTable with two columns:
    ''' - "Description": A string describing the TimeSpan (e.g., "15 Mins", "1 hr").
    ''' - "TimeSpanValue": The corresponding TimeSpan value (e.g., TimeSpan.FromMinutes(15)).
    ''' </returns>
    ''' <remarks>
    ''' The method populates the DataTable with a set of predefined TimeSpan values, such as 15 minutes, 
    ''' 30 minutes, 1 hour, etc. This is typically used to provide a dropdown list of time intervals 
    ''' for filtering or other time-based operations.
    ''' </remarks>
    Private Function GetTimeSpanDataSource() As DataTable
        Dim table As New DataTable()
        table.Columns.Add("Description", GetType(String)) ' Column for the label
        table.Columns.Add("TimeSpanValue", GetType(TimeSpan)) ' Column for the TimeSpan value

        ' Add selectable TimeSpan values
        table.Rows.Add("15 Mins", TimeSpan.FromMinutes(15))
        table.Rows.Add("30 Mins", TimeSpan.FromMinutes(30))
        table.Rows.Add("1 hr", TimeSpan.FromHours(1))
        table.Rows.Add("3 hrs", TimeSpan.FromHours(3))
        table.Rows.Add("6 hrs", TimeSpan.FromHours(6))
        table.Rows.Add("12 hrs", TimeSpan.FromHours(12))
        Return table
    End Function
    ''' <summary>
    ''' Binds a ComboBox to a DataTable containing TimeSpan values and their descriptions.
    ''' The ComboBox displays the descriptions and stores the corresponding TimeSpan values.
    ''' </summary>
    ''' <param name="cmb">The ComboBox control to bind the data source to.</param>
    ''' <remarks>
    ''' This method populates the ComboBox with a list of predefined TimeSpan values (e.g., 15 minutes, 30 minutes, etc.).
    ''' The DataTable used as the data source contains two columns:
    ''' - "Description": A string describing the TimeSpan (e.g., "15 Mins", "1 hr").
    ''' - "TimeSpanValue": The actual TimeSpan value (e.g., TimeSpan.FromMinutes(15)).
    ''' The ComboBox's DisplayMember is set to "Description", and the ValueMember is set to "TimeSpanValue".
    ''' </remarks>
    Private Sub BindTimeSpanComboBox(cmb As ComboBox)
        Dim timeSpanDataSource As DataTable = GetTimeSpanDataSource()
        cmb.DataSource = timeSpanDataSource
        cmb.DisplayMember = "Description" ' Display the description in the dropdown
        cmb.ValueMember = "TimeSpanValue" ' Store the TimeSpan value
    End Sub

    Private Sub ComboBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox1.SelectedIndexChanged
        ApplyAgeFilter(sender)
    End Sub
    ''' <summary>
    ''' Filters the rows in the DataGridView's underlying DataTable based on the selected TimeSpan from the ComboBox.
    ''' Removes rows where the combined Date and Time values are older than the calculated cutoff DateTime.
    ''' </summary>
    ''' <param name="sender">The control that triggered the method, expected to be ComboBox1.</param>
    ''' <remarks>
    ''' This method is triggered by the ComboBox1.SelectedIndexChanged event or can be called explicitly.
    ''' It calculates a cutoff DateTime by subtracting the selected TimeSpan from the current UTC time.
    ''' Rows in the DataTable with a DateTime older than the cutoff are removed, and the DataGridView is refreshed.
    ''' </remarks>
    Private Sub ApplyAgeFilter(sender As Object)
        Dim selectedTimeSpan As TimeSpan
        Dim cmb As ComboBox = CType(sender, ComboBox)
        If cmb.SelectedValue IsNot Nothing Then
            ' Handle the case where SelectedValue is a DataRowView
            If TypeOf cmb.SelectedValue Is TimeSpan Then
                selectedTimeSpan = CType(cmb.SelectedValue, TimeSpan)
            ElseIf TypeOf cmb.SelectedItem Is DataRowView Then
                Dim rowView As DataRowView = CType(cmb.SelectedItem, DataRowView)
                selectedTimeSpan = CType(rowView("TimeSpanValue"), TimeSpan)
            Else
                AppendTextSafe("Error: Unable to extract TimeSpan from SelectedValue." & vbCrLf)
                Return
            End If
            ' Delete any spots older than age

            ' Calculate the cutoff DateTime
            Dim cutoffDateTime As DateTime = DateTime.UtcNow.Subtract(selectedTimeSpan)

            ' Get the DataTable from the DataGridView's DataSource
            Dim dt As DataTable = CType(DataGridView1.DataSource, DataTable)

            ' Check if the DataTable is not null
            If dt IsNot Nothing Then
                ' Use a list to store rows to delete (to avoid modifying the collection while iterating)
                Dim rowsToDelete As New List(Of DataRow)()

                ' Iterate through the rows of the DataTable
                For Each row As DataRow In dt.Rows
                    ' Combine the Date and Time columns into a DateTime object
                    Dim rowDate As DateTime = CType(row("Date"), DateTime)
                    ' Extract hours and minutes
                    Dim hours As Integer = Integer.Parse(row("Time").Substring(0, 2)) ' First 2 characters are hours
                    Dim minutes As Integer = Integer.Parse(row("Time").Substring(2, 2)) ' Last 2 characters are minutes
                    Dim rowTime = New TimeSpan(hours, minutes, 0)

                    Dim rowDateTime As DateTime = rowDate.Add(rowTime)

                    ' Check if the row is older than the cutoff
                    If rowDateTime < cutoffDateTime Then
                        rowsToDelete.Add(row)
                    End If
                Next

                ' Remove the rows from the DataTable
                For Each rowToDelete As DataRow In rowsToDelete
                    dt.Rows.Remove(rowToDelete)
                Next

                ' Refresh the DataGridView
                DataGridView1.Refresh()
            End If
        End If
    End Sub
End Class
