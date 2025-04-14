Imports System.Net.Sockets
Imports System.Net
Imports System.Threading
Imports System.ComponentModel
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar

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
    Dim CallList As New List(Of String)     ' list of calls to poll
    Private PollingTimer As System.Timers.Timer     ' timer to poll cluster
    Private ResponseReceivedEvent As New Threading.ManualResetEvent(False)
    Dim buffer As String = "", cr As Integer
    Dim ClusterInitialized As Boolean = False       ' true when cluster config complete

    ' Handles the Load event for frmCluster
    Private Async Sub frmCluster_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Create data source

        Dim DisplayOrder() As String = {"CC11", "DX Call", "Frequency", "Band", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid"}    ' the order the columns are displayed
        With DataGridView1
            .AutoGenerateColumns = True
            .RowHeadersVisible = False      ' hide the Select column
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
            .Sort(DataGridView1.Columns("Date"), ListSortDirection.Descending)
            .Sort(DataGridView1.Columns("Time"), ListSortDirection.Descending)
            .ColumnHeadersDefaultCellStyle.Font = New Font(DataGridView1.Font, FontStyle.Bold) ' Make the header row bold
            .Columns("Date").DefaultCellStyle.Format = "dd-MMM"
            ' Apply the display order
            If DisplayOrder.Length <> .DataSource.columns.count Then Throw New Exception("There needs to be 1 entry in DisplayOrder for every column in the datasource")
            For i As Integer = 0 To DisplayOrder.Length - 1
                .Columns(DisplayOrder(i)).DisplayIndex = i
            Next
            .Refresh()
        End With

        PollingTimer = New Timers.Timer With {
            .AutoReset = True ' Ensure the timer repeats
            }
        AddHandler PollingTimer.Elapsed, AddressOf OnPollingTimerElapsed

        ' Create the Age drop down
        BindTimeSpanComboBox(ComboBox1, AddressOf GetTimeSpanDataSource)
        ComboBox1.SelectedIndex = ComboBox1.DataSource.Rows.Count - 1 ' Set the selected index to the last item
        ' Create the Update drop down
        BindTimeSpanComboBox(ComboBox2, AddressOf GetUpdateSpanDataSource)
        ComboBox2.SelectedIndex = ComboBox2.DataSource.Rows.Count - 1 ' Set the selected index to the last item

        ' Connect to the DX cluster server
        Cluster.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, True)
        Await Cluster.ConnectAsync(DXcluster, port).ConfigureAwait(False)
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
    End Sub

    ' Handles the DoWork event for the BackgroundWorker
    Private Async Sub Bgw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles Bgw.DoWork
        Try
            While Not Bgw.CancellationPending ' Keep running until the worker is cancelled

                If Cluster.Client.Available > 0 Then
                    Debug.WriteLine("Data available from server. Setting ResponseReceivedEvent.")
                    Dim byt_to_receive(Cluster.Available - 1) As Byte
                    Cluster.Client.Receive(byt_to_receive, 0, byt_to_receive.Length, SocketFlags.None)
                    Dim String_From_Byte As String = System.Text.Encoding.ASCII.GetString(byt_to_receive)
                    buffer += String_From_Byte
                    Debug.WriteLine($"Received: {buffer}")

                    ' Signal that a response has been received
                    ResponseReceivedEvent.Set()

                    If Not LoggedIn Then
                        ' Look for Login: , password: without CR
                        If buffer.EndsWith("login: ") Then
                            AppendTextSafe(buffer)
                            Await SendClusterAsync($"{ClusterUsername}{vbCrLf}") ' Send username
                        ElseIf buffer.EndsWith("password: ") Then
                            AppendTextSafe(buffer)
                            Await SendClusterAsync($"{ClusterPassword}{vbCrLf}") ' Send password
                        ElseIf buffer.Contains(">>") Then
                            ' We are logged in
                            AppendTextSafe(buffer)
                            LoggedIn = True
                            ' Run InitializeCluster in a separate task
                            If Not ClusterInitialized Then
#Disable Warning BC42358 ' Because this call is not awaited, execution of the current method continues before the call is completed
                                Task.Run(Function() InitializeCluster())
#Enable Warning BC42358 ' Because this call is not awaited, execution of the current method continues before the call is completed
                            End If
                        End If
                    End If
                    ' Process the incoming data
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
            End While
        Catch ex As Exception
            AppendTextSafe($"Error in DoWork: {ex.Message}{vbCrLf}")
        End Try
    End Sub
    ''' <summary>
    ''' Initializes the DX cluster with predefined settings and starts the polling timer.
    ''' This method ensures that the cluster is configured only once and prevents multiple initializations.
    ''' </summary>
    ''' <returns>A Task representing the asynchronous operation.</returns>
    Private Async Function InitializeCluster() As Task
        If ClusterInitialized Then Return ' Prevent multiple initializations
        Await SendClusterAsync($"set/name Marc{vbCrLf}")
        Await SendClusterAsync($"set/qth Donvale{vbCrLf}")
        Await SendClusterAsync($"set/qra QF22oe{vbCrLf}")
        Await SendClusterAsync($"unset/echo{vbCrLf}")
        Await SendClusterAsync($"set/prompt >>{vbCrLf}")
        Await SendClusterAsync($"set/ve7cc{vbCrLf}")
        Await SendClusterAsync($"unset/wcy{vbCrLf}")
        Await SendClusterAsync($"unset/wwv{vbCrLf}")
        Await SendClusterAsync($"sh/filter{vbCrLf}")
        Await SendClusterAsync($"clear/spots all{vbCrLf}")
        Await SendClusterAsync($"sh/filter{vbCrLf}")

        ' Start the polling timer
        If Not PollingTimer.Enabled Then
            PollingTimer.Start()
            PollCluster()
        End If
        ClusterInitialized = True
    End Function
    Private Sub OnPollingTimerElapsed(sender As Object, e As Timers.ElapsedEventArgs)
        PollCluster()
    End Sub
    ''' <summary>
    ''' Handles the polling logic for the DX cluster server at regular intervals.
    ''' This method is triggered by the PollingTimer's Elapsed event.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically the PollingTimer.</param>
    ''' <param name="e">The ElapsedEventArgs containing the event data.</param>
    ''' <remarks>
    ''' This method performs the following actions:
    ''' - Stops the timer if the user is not logged in or if the BackgroundWorker is canceled.
    ''' - Iterates through the list of callsigns (CallList) and sends a "sh/dx" command to the cluster server for each callsign.
    ''' - Waits for a response from the server with a timeout of 5 seconds for each command.
    ''' - Logs a message if no response is received for a callsign.
    ''' - Applies the age filter to the DataGridView to remove outdated spots.
    ''' The method ensures that the polling logic is executed at regular intervals without blocking the main thread.
    ''' </remarks>
    Private Async Sub PollCluster()
        If Not LoggedIn OrElse Bgw.CancellationPending Then
            PollingTimer.Stop() ' Stop the timer if not logged in or if cancellation is requested
            Return
        End If

        ' Create list of callsigns to poll from StartUp list, and current callsign
        CallList.Clear()
        If My.Settings.OnStartup <> "" Then CallList = Split(My.Settings.OnStartup, ",").ToList
        If Form1.TextBox1.Text <> "" Then CallList.Add(Form1.TextBox1.Text)
        ' Perform polling logic
        For Each callsign In CallList
            ' Send the sh/dx command
            Await SendClusterAsync($"sh/dx {callsign}{vbCrLf}")
        Next

        ' get the open WSI dialogs for highlighting of the DataGridView
        Form1.OpenWSIDialogs = GetOpenWSIDialogs()

        ' Apply the age filter
        ApplyAgeFilter(ComboBox1)

    End Sub
    Private Sub ProcessMessage(message As String)
        ' Log the message to TextBox1
        AppendTextSafe($"<-- {message}{vbCrLf}")   ' display the message in the TextBox
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
                    ' Check that the callsign is in the list. Sometimes some unasked for ones are present
                    If CallList.Contains(columns(2)) Then
                        Dim row As DataRow = dt.NewRow() ' Create a new DataRow
                        ' Update the DataGridView with new data
                        For i As Integer = 0 To columns.Length - 1 ' Loop through the columns
                            If i < dt.Columns.Count Then
                                row(i) = columns(i) ' Fill the DataRow with data
                            End If
                        Next
                        row("Band") = FreqToBand(CSng(row("Frequency")))      ' create band column
                        dt.Rows.Add(row) ' Add the new row to the DataTable
                    End If
                End If
                DataGridView1.Refresh() ' Refresh the DataGridView to show the new data
            End If
        End If
    End Sub

    ''' <summary>
    ''' Converts a given frequency to its corresponding amateur radio band.
    ''' </summary>
    ''' <param name="freq">The frequency in kHz.</param>
    ''' <returns>A string representing the amateur radio band (e.g., "20m", "40m"). Returns "Unknown" if the frequency does not match any predefined band.</returns>
    Private Shared ReadOnly FrequencyBands As New Dictionary(Of (Integer, Integer), String) From {
    {(1800, 2000), "160m"},
    {(3500, 4000), "80m"},
    {(7000, 7300), "40m"},
    {(10100, 10150), "30m"},
    {(14000, 14350), "20m"},
    {(18068, 18168), "17m"},
    {(21000, 21450), "15m"},
    {(24890, 24990), "12m"},
    {(28000, 29700), "10m"},
    {(50000, 54000), "6m"},
    {(144000, 148000), "2m"},
    {(222000, 225000), "1.25m"},
    {(420000, 450000), "70cm"}
}
    Shared Function FreqToBand(freq As Single) As String
        For Each band In FrequencyBands
            If freq >= band.Key.Item1 AndAlso freq <= band.Key.Item2 Then
                Return band.Value
            End If
        Next
        Return "Unknown"
    End Function
    ''' <summary>
    ''' Retrieves a dictionary of open WSI dialog forms.
    ''' </summary>
    ''' <returns>
    ''' A dictionary where the key is the name of the form and the value is the WSI form instance.
    ''' </returns>
    ''' <remarks>
    ''' This method iterates through all open forms in the application and filters out those of type WSI.
    ''' It is useful for managing and interacting with multiple WSI dialog instances.
    ''' </remarks>
    Public Shared Function GetOpenWSIDialogs() As Dictionary(Of String, Form)
        Dim wsiDialogs As New Dictionary(Of String, Form)
        For Each frm As Form In Application.OpenForms
            If TypeOf frm Is WSI Then
                Dim t = frm.Text
                wsiDialogs.Add(frm.Text, CType(frm, WSI))
            End If
        Next
        Return wsiDialogs
    End Function

    ''' <summary>
    ''' Sends a command or message to the DX cluster server over a TCP connection.
    ''' </summary>
    ''' <param name="msg">The message or command to send to the cluster server.</param>
    ''' <remarks>
    ''' This method converts the provided message into a byte array and sends it to the cluster server
    ''' using the established TCP connection. It also appends the message to the TextBox for logging purposes.
    ''' If an error occurs during the send operation, the error is logged to the TextBox.
    ''' </remarks>
    Async Function SendClusterAsync(msg As String) As Task
        Try
            ' Reset the event before waiting for a response
            ResponseReceivedEvent.Reset()

            ' Convert the command to bytes and send it to the cluster
            Dim byt_to_send() As Byte = System.Text.Encoding.ASCII.GetBytes($"{msg}")
            Await Cluster.GetStream().WriteAsync(byt_to_send)

            ' Append the command to the TextBox for display
            AppendTextSafe($"-> {msg}")
            Debug.Write($"Sent: {msg}")

            ' Wait for response from cluster
            If Not ResponseReceivedEvent.WaitOne(5000) Then
                AppendTextSafe($"SendCluster: no response to command{vbCrLf}")
            Else
                Debug.WriteLine("Response received.")
            End If
            Thread.Sleep(200) ' Small delay to allow for processing
        Catch ex As Exception
            AppendTextSafe($"Error in SendCluster: {ex.Message}{vbCrLf}")
        End Try
    End Function
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

        Dim table As New DataTable(), ColumnHeadings() As String = {"CC11", "Frequency", "DX Call", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid", "Band"}

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
        Try
            If TextBox1.InvokeRequired Then
                TextBox1.Invoke(New Action(Of String)(AddressOf AppendTextSafe), text)
                Return
            End If

            ' Append the new text
            TextBox1.AppendText(text)

            Const maxLines As Integer = 50  ' Limit the number of lines to 50
            Dim lines As String() = TextBox1.Lines
            If lines.Length > maxLines Then
                TextBox1.Lines = lines.Skip(lines.Length - maxLines).ToArray()
            End If
            ' Scroll to the last line
            TextBox1.SelectionStart = TextBox1.Text.Length
            TextBox1.ScrollToCaret()
        Catch ex As Exception
            Debug.WriteLine($"Error in AppendTextSafe: {ex.Message}")
        End Try
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
        table.Rows.Add("15 mins", TimeSpan.FromMinutes(15))
        table.Rows.Add("30 mins", TimeSpan.FromMinutes(30))
        table.Rows.Add("1 hr", TimeSpan.FromHours(1))
        table.Rows.Add("3 hrs", TimeSpan.FromHours(3))
        table.Rows.Add("6 hrs", TimeSpan.FromHours(6))
        Return table
    End Function
    ''' Creates and returns a DataTable containing predefined TimeSpan values and their descriptions.
    ''' This DataTable is used as the data source for a ComboBox to allow users to select an update interval.
    ''' </summary>
    ''' <returns>
    ''' A DataTable with two columns:
    ''' - "Description": A string describing the TimeSpan (e.g., "20 secs", "1 min").
    ''' - "TimeSpanValue": The corresponding TimeSpan value (e.g., TimeSpan.FromSeconds(20)).
    ''' </returns>
    ''' <remarks>
    ''' The method populates the DataTable with a set of predefined TimeSpan values, such as 20 seconds, 
    ''' 1 minute, 5 minutes, etc. This is typically used to provide a dropdown list of update intervals 
    ''' for refreshing or polling operations.
    ''' </remarks>
    Private Function GetUpdateSpanDataSource() As DataTable
        Dim table As New DataTable()
        table.Columns.Add("Description", GetType(String)) ' Column for the label
        table.Columns.Add("TimeSpanValue", GetType(TimeSpan)) ' Column for the TimeSpan value

        ' Add selectable TimeSpan values
        table.Rows.Add("20 secs", TimeSpan.FromSeconds(20))
        table.Rows.Add("1 min", TimeSpan.FromMinutes(1))
        table.Rows.Add("5 mins", TimeSpan.FromMinutes(5))
        table.Rows.Add("10 mins", TimeSpan.FromMinutes(10))
        table.Rows.Add("1 hr", TimeSpan.FromHours(1))
        Return table
    End Function
    ''' <summary>
    ''' Binds a ComboBox to a DataTable containing TimeSpan values and their descriptions.
    ''' The ComboBox displays the descriptions and stores the corresponding TimeSpan values.
    ''' </summary>
    ''' <param name="cmb">The ComboBox control to bind the data source to.</param>
    ''' <param name="GetDataSource">A delegate function that returns a DataTable containing the data to bind.</param>
    ''' <remarks>
    ''' This method dynamically binds a ComboBox to a data source provided by the `GetDataSource` function.
    ''' The DataTable must contain two columns:
    ''' - "Description": A string describing the TimeSpan (e.g., "15 Mins", "1 hr").
    ''' - "TimeSpanValue": The actual TimeSpan value (e.g., TimeSpan.FromMinutes(15)).
    ''' The ComboBox's DisplayMember is set to "Description", and the ValueMember is set to "TimeSpanValue".
    ''' This allows the ComboBox to display user-friendly labels while storing the corresponding TimeSpan values.
    ''' </remarks>
    Private Sub BindTimeSpanComboBox(cmb As ComboBox, GetDataSource As Func(Of DataTable))
        Dim timeSpanDataSource As DataTable = GetDataSource()
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
        ' Ensure cross-thread protection for ComboBox1
        If sender.InvokeRequired Then
            sender.Invoke(New Action(Of Object)(AddressOf ApplyAgeFilter), sender)
            Return
        End If
        ' Check if the sender is a ComboBox and has a selected value
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

    Private Sub ComboBox2_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox2.SelectedIndexChanged
        ApplyUpdateInterval(sender)
    End Sub
    ''' <summary>
    ''' Updates the polling interval for the PollingTimer based on the selected TimeSpan from ComboBox2.
    ''' Immediately triggers a cluster poll and adjusts the timer interval for subsequent polls.
    ''' </summary>
    ''' <param name="sender">The control that triggered the method, expected to be ComboBox2.</param>
    ''' <remarks>
    ''' This method is triggered by the ComboBox2.SelectedIndexChanged event or can be called explicitly.
    ''' It retrieves the selected TimeSpan from ComboBox2, validates the selection, and updates the PollingTimer's interval.
    ''' Additionally, it triggers an immediate poll of the cluster to reflect the new interval change.
    ''' The method ensures thread safety by invoking itself on the UI thread if necessary.
    ''' </remarks>
    Sub ApplyUpdateInterval(sender As Object)
        ' Ensure cross-thread protection
        If ComboBox2.InvokeRequired Then
            ComboBox2.Invoke(New Action(Of Object)(AddressOf ApplyUpdateInterval), sender)
            Return
        End If

        ' Retrieve the selected TimeSpan from ComboBox2
        Dim selectedTimeSpan As TimeSpan
        If ComboBox2.SelectedValue IsNot Nothing Then
            If TypeOf ComboBox2.SelectedValue Is TimeSpan Then
                selectedTimeSpan = CType(ComboBox2.SelectedValue, TimeSpan)
            ElseIf TypeOf ComboBox2.SelectedItem Is DataRowView Then
                Dim rowView As DataRowView = CType(ComboBox2.SelectedItem, DataRowView)
                selectedTimeSpan = CType(rowView("TimeSpanValue"), TimeSpan)
            Else
                AppendTextSafe("Error: Unable to extract TimeSpan from ComboBox2.SelectedValue." & vbCrLf)
                Return
            End If
        Else
            AppendTextSafe("Error: No value selected in ComboBox2." & vbCrLf)
            Return
        End If
        PollCluster()       ' poll cluster when update interval is changed
        PollingTimer.Interval = selectedTimeSpan.TotalMilliseconds  ' Update the timer interval
    End Sub
    ''' <summary>
    ''' Handles the RowPrePaint event for DataGridView1.
    ''' Dynamically highlights rows in the DataGridView based on whether a matching 
    ''' callsign and band exist in the WSI dialog's DataGridView.
    ''' </summary>
    ''' <param name="sender">The DataGridView that triggered the event.</param>
    ''' <param name="e">Provides data for the RowPrePaint event.</param>
    ''' <remarks>
    ''' This method performs the following actions:
    ''' - Retrieves the current row being painted.
    ''' - Extracts the "DX Call" and "Band" values from the row.
    ''' - Checks for a matching entry in the WSI dialog's DataGridView using the extracted values.
    ''' - If the WSI dialog contains a DataGridView with a matching "Band" column, the row's background 
    '''   is reset to white, indicating a previous contact on this band.
    ''' - If no matching "Band" column is found, the row is highlighted with a green background, 
    '''   indicating no previous contact on this band.
    ''' - Throws an exception if the DataGridView1 control cannot be located in the WSI dialog.
    ''' </remarks>

    Private Sub DataGridView1_RowPrePaint(sender As Object, e As DataGridViewRowPrePaintEventArgs) Handles DataGridView1.RowPrePaint
        Dim dgv As DataGridView = CType(sender, DataGridView)
        ' Get the current row
        Dim row As DataGridViewRow = dgv.Rows(e.RowIndex)
        If Form1.OpenWSIDialogs IsNot Nothing Then
            Dim wsiDialog = Form1.OpenWSIDialogs(row.Cells("DX Call").Value)    ' get WSI dialog for this call
            If wsiDialog IsNot Nothing Then
                Dim dgv1 = CType(FindControlRecursive(wsiDialog.Controls(0), "DataGridView1"), DataGridView)    ' find the DataGridView control
                If dgv1 Is Nothing Then
                    Throw New Exception($"Could not locate DataGridView1 in {wsiDialog.Name}")
                Else
                    If dgv1.DataSource IsNot Nothing Then
                        ' Get the DataTable from wsi form DataGridView1.DataSource
                        Dim dt As DataTable = TryCast(dgv1.DataSource, DataTable)
                        ' Check for any value (W, C, CL) in band column, i.e. column exists
                        If dt.Columns.Contains($"BAND_{row.Cells("Band").Value}") Then
                            ' Reset the background color if previous contact on this band
                            row.DefaultCellStyle.BackColor = Color.White
                        Else
                            ' Highlight the row if no contact on this band
                            row.DefaultCellStyle.BackColor = Color.LightGreen
                        End If
                    End If
                End If
            End If
        End If
    End Sub
    ''' <summary>
    ''' Recursively searches for a control with the specified name within a parent control's hierarchy.
    ''' </summary>
    ''' <param name="parent">The parent control to start the search from.</param>
    ''' <param name="name">The name of the control to search for.</param>
    ''' <returns>
    ''' The control with the specified name if found; otherwise, Nothing.
    ''' </returns>
    ''' <remarks>
    ''' This method is useful for locating controls that may be nested within containers 
    ''' (e.g., Panels, GroupBoxes) and are not directly accessible from the top-level Controls collection.
    ''' </remarks>
    Private Function FindControlRecursive(parent As Control, name As String) As Control
        For Each ctrl As Control In parent.Controls
            If ctrl.Name = name Then Return ctrl
            Dim found = FindControlRecursive(ctrl, name)
            If found IsNot Nothing Then Return found
        Next
        Return Nothing
    End Function
End Class
