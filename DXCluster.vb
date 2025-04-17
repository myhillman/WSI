Imports System.Net.Sockets
Imports System.ComponentModel
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar
Imports System.Text
Imports System.Collections.Concurrent

''' <summary>
''' Represents the main form for interacting with a DX cluster server.
''' This form provides functionality for connecting to the server, retrieving DX spots, 
''' and displaying them in a DataGridView. It also allows users to filter and manage 
''' amateur radio bands and configure polling intervals.
''' </summary>
''' <remarks>
''' The frmCluster class includes the following key features:
''' - **TCP Connection**: Establishes and manages a TCP connection to the DX cluster server.
''' - **BackgroundWorker**: Handles asynchronous communication with the server to avoid blocking the UI thread.
''' - **Polling Timer**: Periodically polls the server for updates based on user-configured intervals.
''' - **DataGridView**: Displays DX cluster data, including frequency, callsign, date, time, and other metadata.
''' - **Amateur Band Management**: Allows users to select and save amateur bands for filtering displayed data.
''' - **Cluster Initialization**: Configures the DX cluster with predefined settings upon connection.
''' - **Thread-Safe UI Updates**: Ensures safe updates to UI controls from background threads.
''' - **Resource Cleanup**: Properly disposes of resources (e.g., timers, connections) when the form is closed.
''' This form is designed for amateur radio operators who want to monitor and interact with DX cluster data.
''' It provides a user-friendly interface for managing connections, filtering data, and customizing settings.
''' </remarks>

Public Class frmCluster

    ' Flags to track the state of the connection and operations
    Private LoggedIn As Boolean = False
    Private PollingTimer As System.Timers.Timer     ' timer to poll cluster
    Private buffer As String = "", cr As Integer
    Dim ClusterInitialized As Boolean = False       ' true when cluster config complete
    Private OpenWSIDialogs As ConcurrentDictionary(Of String, Form) ' Dictionary to store open WSI dialogs
    Private soundPlayer As New SoundPlayerHelper()      ' Load the sound player
    Private clusterManager As New ClusterManager()

    ''' <summary>
    ''' Saves the selected amateur bands to My.Settings whenever a checkbox is changed.
    ''' </summary>
    Private Sub SaveAmateurBands()
        ' Find the GroupBox containing the checkboxes
        Dim groupBox = Me.Controls.OfType(Of GroupBox)().FirstOrDefault(Function(g) g.Text = AmateurBandsGroupBoxText)
        If groupBox IsNot Nothing Then
            ' Get all checked checkboxes in the GroupBox
            Dim selectedBands = groupBox.Controls.OfType(Of CheckBox)().
                            Where(Function(cb) cb.Checked).
                            Select(Function(cb) cb.Text).
                            ToList()

            ' Save the selected bands as a comma-separated string in My.Settings
            My.Settings.AmateurBands = String.Join(",", selectedBands)
            My.Settings.Save()
        End If
    End Sub

#Region "Initialization"
    ''' <summary>
    ''' Initializes the DX cluster with predefined settings and starts the polling timer.
    ''' This method ensures that the cluster is configured only once and prevents multiple initializations.
    ''' </summary>
    ''' <returns>A Task representing the asynchronous operation.</returns>
    Private Async Function InitializeCluster() As Task
        ' Connect to the cluster
        If ClusterInitialized Then Return ' Prevent multiple initializations
        Await clusterManager.ConnectAsync()         ' connect to cluster
        Await SendClusterCommand($"{ClusterUsername}{vbCrLf}")  ' send username
        LoggedIn = True
        ' Send a series of commands and wait for ">>" prompt
        Await SendClusterCommand($"unset/echo{vbCrLf}")
        Await SendClusterCommand($"set/prompt >>{vbCrLf}")
        Await SendClusterCommand($"set/name Marc{vbCrLf}")
        Await SendClusterCommand($"set/qth Donvale{vbCrLf}")
        Await SendClusterCommand($"set/qra QF22oe{vbCrLf}")
        Await SendClusterCommand($"set/ve7cc{vbCrLf}")
        Await SendClusterCommand($"unset/wcy{vbCrLf}")
        Await SendClusterCommand($"unset/wwv{vbCrLf}")
        Await SendClusterCommand($"sh/filter{vbCrLf}")
        Await SendClusterCommand($"clear/spots all{vbCrLf}")
        Await SendClusterCommand($"sh/filter{vbCrLf}")
        ClusterInitialized = True

        ' Start the polling timer
        ' Update the polling interval
        InvokeIfRequired(ComboBox2, AddressOf SetPollingInterval)

        ' Start the timer if not already running
        If Not PollingTimer.Enabled Then
            PollingTimer.Start()
        End If

        ' Optionally trigger an immediate poll
        PollCluster()

        ClusterInitialized = True
    End Function
    Private Async Function SendClusterCommand(command As String) As Task
        Dim response = Await clusterManager.SendCommandAsync(command)
        AppendTextSafe(TextBox1, response)
    End Function

    ''' <summary>
    ''' Sets the PollingTimer interval based on the selected value in ComboBox2.
    ''' Ensures thread-safe access and validates the selected value.
    ''' </summary>
    Private Sub SetPollingInterval()
        ' Validate and set the interval
        If ComboBox2.SelectedValue IsNot Nothing AndAlso TypeOf ComboBox2.SelectedValue Is TimeSpan Then
            Dim selectedTimeSpan As TimeSpan = CType(ComboBox2.SelectedValue, TimeSpan)
            PollingTimer.Interval = selectedTimeSpan.TotalMilliseconds
            Debug.WriteLine($"Polling interval set to {PollingTimer.Interval / 1000} s.")
        Else
            AppendTextSafe(TextBox1, "Error: Invalid or null SelectedValue in ComboBox2." & vbCrLf)
        End If
    End Sub
    Private Sub OnPollingTimerElapsed(sender As Object, e As Timers.ElapsedEventArgs)
        PollCluster()
    End Sub
    ''' <summary>
    ''' Handles the polling logic for the DX cluster server at regular intervals.
    ''' This method is triggered by the PollingTimer's Elapsed event and performs the following actions:
    ''' - Sends polling commands to the DX cluster server for a list of callsigns.
    ''' - Waits for responses from the server and processes them.
    ''' - Updates the DataGridView with the latest data and applies filters.
    ''' </summary>
    ''' <remarks>
    ''' Key steps performed by this method:
    ''' 1. **Connection Check**:
    '''    - Stops the polling timer if the user is not logged in or if the BackgroundWorker is canceled.
    ''' 2. **Callsign List Preparation**:
    '''    - Creates a list of callsigns to poll, combining the startup list and the current callsign.
    ''' 3. **Polling Logic**:
    '''    - Iterates through the list of callsigns and sends a "sh/dx" command to the cluster server for each.
    ''' 4. **Response Handling**:
    '''    - Waits for responses from the server and processes them asynchronously.
    ''' 5. **UI Updates**:
    '''    - Retrieves open WSI dialogs and highlights relevant rows in the DataGridView.
    '''    - Applies the age filter to remove outdated spots from the DataGridView.
    ''' 
    ''' This method ensures that polling is performed efficiently and updates the UI with the latest data
    ''' without blocking the main thread.
    ''' </remarks>
    Private Async Sub PollCluster()

        If Not LoggedIn Then
            PollingTimer.Stop() ' Stop the timer if not logged in or if cancellation is requested
            Return
        End If

        ' Get list of all active WSI dialogs
        GetOpenWSIDialogs()

        ' Perform polling logic
        For Each kvp In OpenWSIDialogs
            ' Send the sh/dx command and wait for a burst of data
            Dim response = Await clusterManager.SendCommandAsync($"sh/dx {kvp.Key}{vbCrLf}", isMultiline:=True)
            If Not String.IsNullOrWhiteSpace(response) Then
                ' Pull apart buffer into messages
                Dim messages As String() = response.Split(New String() {vbCrLf}, StringSplitOptions.RemoveEmptyEntries)
                For Each message In messages
                    If Not String.IsNullOrWhiteSpace(message) Then
                        ' Process each message
                        ProcessMessage(message)
                    End If
                Next
            End If
        Next

        ApplyAgeFilter(ComboBox1)  ' Apply the age filter
    End Sub
    ''' <summary>
    ''' Ensures that the specified action is executed on the UI thread.
    ''' If the calling thread is not the UI thread, the action is invoked on the UI thread.
    ''' Otherwise, the action is executed directly.
    ''' </summary>
    ''' <param name="control">The control used to check the thread context.</param>
    ''' <param name="action">The action to execute, either directly or via invocation on the UI thread.</param>
    ''' <remarks>
    ''' This method is useful for safely updating UI elements from a background thread.
    ''' It checks the `InvokeRequired` property of the control to determine if the current thread
    ''' is different from the UI thread. If so, it uses the `Invoke` method to marshal the action
    ''' to the UI thread. Otherwise, it executes the action directly.
    ''' 
    ''' Example usage:
    ''' <code>
    ''' InvokeIfRequired(myControl, Sub() myControl.Text = "Updated Text")
    ''' </code>
    ''' </remarks>
    Private Sub InvokeIfRequired(control As Control, action As Action)
        If control.InvokeRequired Then
            control.Invoke(action)
        Else
            action()
        End If
    End Sub
    Private Sub ProcessMessage(message As String)
        ' Log the message to TextBox1
        AppendTextSafe(TextBox1, $"<-- {message}{vbCrLf}")   ' display the message in the TextBox
        Debug.WriteLine($"Processing Message: {message}")
        ' Handle cluster data (e.g., CC11 format)
        If message.Contains("^"c) Then
            ' Parse and display cluster data
            ParseClusterData(message)
        End If
    End Sub

    Public Shared ReadOnly FrequencyBands As New Dictionary(Of (Integer, Integer), String) From {
    {(1800, 2000), "160m"},
    {(3500, 4000), "80m"},
    {(5351, 5366), "60m"},
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
    ''' <summary>
    ''' Converts a given frequency to its corresponding amateur radio band.
    ''' </summary>
    ''' <param name="freq">The frequency in kHz.</param>
    ''' <returns>A string representing the amateur radio band (e.g., "20m", "40m"). Returns "Unknown" if the frequency does not match any predefined band.</returns>
    Shared Function FreqToBand(freq As Single) As String
        For Each band In FrequencyBands
            If freq >= band.Key.Item1 AndAlso freq <= band.Key.Item2 Then
                Return band.Value
            End If
        Next
        Return "Unknown"
    End Function
    ''' <summary>
    ''' Infers the mode (Digital, CW, Phone) based on the given amateur frequency.
    ''' </summary>
    ''' <param name="frequency">The frequency in kHz.</param>
    ''' <returns>A string representing the mode (e.g., "Digital", "CW", "Phone").</returns>
    Public Shared Function InferMode(frequency As Single) As String
        ' Define frequency ranges for each mode
        Dim modeRanges As New Dictionary(Of String, List(Of (Single, Single))) From {
        {"Digital", New List(Of (Single, Single)) From {
            (1838, 1840), ' 160m Digital
            (3580, 3600), ' 80m Digital
            (7035, 7045), ' 40m Digital
            (10130, 10150), ' 30m Digital
            (14070, 14099), ' 20m Digital
            (18100, 18110), ' 17m Digital
            (21070, 21100), ' 15m Digital
            (24910, 24930), ' 12m Digital
            (28120, 28150)  ' 10m Digital
        }},
        {"CW", New List(Of (Single, Single)) From {
            (1800, 1840), ' 160m CW
            (3500, 3600), ' 80m CW
            (7000, 7040), ' 40m CW
            (10100, 10150), ' 30m CW
            (14000, 14070), ' 20m CW
            (18068, 18100), ' 17m CW
            (21000, 21070), ' 15m CW
            (24890, 24910), ' 12m CW
            (28000, 28120)  ' 10m CW
        }},
        {"Phone", New List(Of (Single, Single)) From {
            (1840, 2000), ' 160m Phone
            (3600, 4000), ' 80m Phone
            (7040, 7300), ' 40m Phone
            (14100, 14350), ' 20m Phone
            (18110, 18168), ' 17m Phone
            (21100, 21450), ' 15m Phone
            (24930, 24990), ' 12m Phone
            (28150, 29700)  ' 10m Phone
        }}
    }

        ' Check the frequency against each mode's ranges
        For Each mode In modeRanges
            For Each range In mode.Value
                If frequency >= range.Item1 AndAlso frequency <= range.Item2 Then
                    Return mode.Key
                End If
            Next
        Next

        ' Return "Unknown" if no match is found
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
    Public Shared Function GetOpenWSIDialogs() As ConcurrentDictionary(Of String, Form)
        Dim wsiDialogs As New ConcurrentDictionary(Of String, Form)
        For Each frm As Form In Application.OpenForms
            If TypeOf frm Is WSI Then
                Dim t = frm.Text
                wsiDialogs.TryAdd(frm.Text, frm)
            End If
        Next
        Return wsiDialogs
    End Function

    ''' <summary>
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

    Private Const AmateurBandsGroupBoxText As String = "Amateur Bands"  ' name of group containing Amateur Band check boxes
    Private Const BandColumnName As String = "Band"

    Private Const TimeSpanValueColumnName As String = "TimeSpanValue"

    ''' <summary>
    ''' Retrieves the underlying DataTable from a DataGridView's DataSource.
    ''' </summary>
    ''' <param name="dgv">The DataGridView control whose DataSource is being accessed.</param>
    ''' <returns>
    ''' The DataTable if the DataSource is of type DataTable or DataView; otherwise, throws an exception.
    ''' </returns>
    ''' <exception cref="InvalidOperationException">
    ''' Thrown if the DataSource is not of type DataTable or DataView.
    ''' </exception>
    ''' <remarks>
    ''' This method is useful for extracting the DataTable from a DataGridView, regardless of whether
    ''' the DataSource is directly a DataTable or wrapped in a DataView. It ensures consistent access
    ''' to the underlying data structure for operations like filtering, updating, or iterating through rows.
    ''' </remarks>
    Private Function GetDataTableFromDataGridView(dgv As DataGridView) As DataTable
        If TypeOf dgv.DataSource Is DataTable Then
            Return CType(dgv.DataSource, DataTable)
        ElseIf TypeOf dgv.DataSource Is DataView Then
            Return CType(CType(dgv.DataSource, DataView).Table, DataTable)
        Else
            Throw New InvalidOperationException("Unexpected DataSource type.")
        End If
    End Function
#End Region

#Region "DataGridView Helpers"
    ' Methods related to DataGridView
    ''' <summary>
    ''' Updates the DataGridView's DataSource to filter rows based on the state of the checkboxes.
    ''' </summary>
    Private Sub UpdateDataGridViewFilter()
        SaveAmateurBands() ' Save the selected bands to settings
        ' Get the "Amateur Bands" group box
        Dim groupBox = Me.Controls.OfType(Of GroupBox)().FirstOrDefault(Function(g) g.Text = AmateurBandsGroupBoxText)
        If groupBox Is Nothing Then Return ' Exit if the group box is not found

        ' Get the list of checked bands
        Dim checkedBands = groupBox.Controls.OfType(Of CheckBox)().
                       Where(Function(cb) cb.Checked).
                       Select(Function(cb) cb.Text).
                       ToList()
        ' Get the DataTable from the DataGridView's DataSource
        Dim dt As DataTable = GetDataTableFromDataGridView(DataGridView1)
        If dt IsNot Nothing Then
            dt.DefaultView.RowFilter = String.Join(" OR ", checkedBands.Select(Function(b) $"{BandColumnName} = '{b}'"))
        End If
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

        ' Get the "Amateur Bands" group box
        Dim groupBox = Me.Controls.OfType(Of GroupBox)().FirstOrDefault(Function(g) g.Text = AmateurBandsGroupBoxText)
        If groupBox Is Nothing Then Return ' Exit if the group box is not found

        ' Get the band value from the current row
        Dim band As String = row.Cells("Band").Value?.ToString()
        If String.IsNullOrEmpty(band) Then Return ' Exit if the band value is empty

        If Not OpenWSIDialogs.IsEmpty Then
            Dim wsiDialog = OpenWSIDialogs(row.Cells("DX Call").Value)    ' get WSI dialog for this call
            If wsiDialog IsNot Nothing Then
                Dim dgv1 = CType(FindControlRecursive(wsiDialog.Controls(0), "DataGridView1"), DataGridView)    ' find the DataGridView control
                If dgv1 Is Nothing Then
                    Throw New Exception($"Could not locate DataGridView1 in {wsiDialog.Name}")
                Else
                    If dgv1.DataSource IsNot Nothing Then
                        ' Get the DataTable from wsi form DataGridView1.DataSource
                        Dim dt As DataTable = TryCast(dgv1.DataSource, DataTable)

                        ' if column exists, and it is not empty, then we've had a contact on ths slot
                        Dim ColumnName = $"BAND_{band}"
                        If Not dt.Columns.Contains(ColumnName) OrElse
                           dt.Rows.Count = 0 OrElse
                           String.IsNullOrEmpty(dt.Rows(0)(ColumnName)?.ToString()) Then
                            ' Highlight the row if no contact on this band
                            row.DefaultCellStyle.BackColor = Color.LightGreen
                            ' Play a sound to indicate a new spot
                            If Not soundPlayer.IsSoundPlaying Then
                                soundPlayer.PlayWavWithLimit(My.Settings.Alert, 2 * 1000) ' Plays a notification sound
                            End If
                        Else
                            ' Reset the background color if previous contact on this band
                            row.DefaultCellStyle.BackColor = Color.White
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
            data = data.Trim(vbCr, vbLf)    ' remove rubbish
            Dim columns As String() = data.Split("^"c) ' Split the string into columns
            If columns(0) = "CC11" Then     ' it's a cluster message
                Dim dt As DataTable
                If TypeOf DataGridView1.DataSource Is DataTable Then
                    dt = CType(DataGridView1.DataSource, DataTable)
                ElseIf TypeOf DataGridView1.DataSource Is DataView Then
                    dt = CType(CType(DataGridView1.DataSource, DataView).Table, DataTable)
                Else
                    Throw New InvalidOperationException("Unsupported DataSource type.")
                End If
                Dim row As DataRow = dt.NewRow
                ' Populate the DataRow with values from the columns array
                For i As Integer = 0 To columns.Length - 1
                    If i < dt.Columns.Count Then
                        row(i) = columns(i)
                    End If
                Next

                ' check if row is old
                If SpotIsOld(row) Then Return        ' ignore rows that are old

                ' Check if the DataTable already contains the data
                Dim rows As DataRow() = dt.Select($"Frequency='{row("Frequency")}' AND [DX Call]='{row("DX Call")}' AND Date='{row("Date")}' AND Time='{row("Time")}' AND Spotter='{row("Spotter")}'")
                If rows.Length = 0 Then     ' it does not, so add
                    ' Check that the callsign is in the list. Sometimes some unasked for ones are present
                    If OpenWSIDialogs.ContainsKey(row("DX Call")) Then
                        row("Band") = FreqToBand(CSng(row("Frequency")))      ' create band column
                        row("Mode") = InferMode(CSng(row("Frequency")))      ' create mode column")
                        dt.Rows.Add(row) ' Add the new row to the DataTable
                    End If
                End If
                DataGridView1.Refresh() ' Refresh the DataGridView to show the new data
            End If
        End If
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

        ' Delete any spots older than age

        Dim dt As DataTable = GetDataTableFromDataGridView(DataGridView1)

        ' Check if the DataTable is not null
        If dt IsNot Nothing Then
            ' Use a list to store rows to delete (to avoid modifying the collection while iterating)
            Dim rowsToDelete As New List(Of DataRow)()

            ' Iterate through the rows of the DataTable
            For Each row As DataRow In dt.Rows
                If SpotIsOld(row) Then rowsToDelete.Add(row)
            Next

            ' Remove the rows from the DataTable
            For Each rowToDelete As DataRow In rowsToDelete
                dt.Rows.Remove(rowToDelete)
            Next
            ' Sort the DataTable by Date and Time
            dt.DefaultView.Sort = "Date DESC, Time DESC"
            ' Refresh the DataGridView
            DataGridView1.Refresh()
        End If
    End Sub

    ''' <summary>
    ''' Determines if a DataTable row is considered "old" based on the selected TimeSpan from ComboBox1.
    ''' </summary>
    ''' <param name="row">The DataTable row to evaluate.</param>
    ''' <returns>True if the row is older than the cutoff DateTime; otherwise, False.</returns>
    Private Function SpotIsOld(row As DataRow) As Boolean
        Try
            ' Validate combobox1's selected value
            Dim selectedTimeSpan As TimeSpan
            If ComboBox1.SelectedValue IsNot Nothing Then
                If TypeOf ComboBox1.SelectedValue Is TimeSpan Then
                    selectedTimeSpan = CType(ComboBox1.SelectedValue, TimeSpan)
                ElseIf TypeOf ComboBox1.SelectedItem Is DataRowView Then
                    Dim rowView As DataRowView = CType(ComboBox1.SelectedItem, DataRowView)
                    selectedTimeSpan = CType(rowView("TimeSpanValue"), TimeSpan)
                Else
                    Debug.WriteLine("Error: Unable to extract TimeSpan from combobox1.SelectedValue.")
                    Return False
                End If
            Else
                Debug.WriteLine("Error: combobox1.SelectedValue is null.")
                Return False
            End If

            ' Calculate the cutoff DateTime
            Dim cutoffDateTime As DateTime = DateTime.UtcNow.Subtract(selectedTimeSpan)

            ' Validate and parse the row's Date and Time columns
            If row.Table.Columns.Contains("Date") AndAlso row.Table.Columns.Contains("Time") Then
                Dim rowDate As DateTime
                Dim rowTime As TimeSpan

                ' Parse the Date column
                If Not DateTime.TryParse(row("Date").ToString(), rowDate) Then
                    Debug.WriteLine($"Error: Invalid Date value in row: {row("Date")}")
                    Return False
                End If

                ' Parse the Time column (assumes "HHmm" format)
                Dim timeString As String = row("Time").ToString()
                If Integer.TryParse(timeString.Substring(0, 2), Nothing) AndAlso
               Integer.TryParse(timeString.Substring(2, 2), Nothing) Then
                    Dim hours As Integer = Integer.Parse(timeString.Substring(0, 2))
                    Dim minutes As Integer = Integer.Parse(timeString.Substring(2, 2))
                    rowTime = New TimeSpan(hours, minutes, 0)
                Else
                    Debug.WriteLine($"Error: Invalid Time value in row: {row("Time")}")
                    Return False
                End If

                ' Combine Date and Time into a single DateTime
                Dim rowDateTime As DateTime = rowDate.Add(rowTime)

                ' Check if the row is older than the cutoff
                Return rowDateTime < cutoffDateTime
            Else
                Debug.WriteLine("Error: Row does not contain required 'Date' or 'Time' columns.")
                Return False
            End If
        Catch ex As Exception
            Debug.WriteLine($"Error in SpotIsOld: {ex.Message}")
            Return False
        End Try
    End Function

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

        Dim table As New DataTable(), ColumnHeadings() As String = {"CC11", "Frequency", "DX Call", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid", "Band", "Mode"}

        ' Add column headings
        For i As Integer = 0 To ColumnHeadings.Length - 1
            table.Columns.Add(ColumnHeadings(i))
        Next
        table.Columns("Frequency").DataType = GetType(Single)
        table.Columns("Date").DataType = GetType(Date)

        Return table
    End Function
#End Region

#Region "ComboBox Helpers"
    ' Methods related to ComboBox
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
        With cmb
            .DataSource = timeSpanDataSource
            .DisplayMember = "Description" ' Display the description in the dropdown
            .ValueMember = "TimeSpanValue" ' Store the TimeSpan value
        End With
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
    Private Function GetAgeSpanDataSource() As DataTable
        Dim table As New DataTable()
        With table
            .Columns.Add("Description", GetType(String)) ' Column for the label
            .Columns.Add("TimeSpanValue", GetType(TimeSpan)) ' Column for the TimeSpan value

            ' Add selectable TimeSpan values
            .Rows.Add("15 mins", TimeSpan.FromMinutes(15))
            .Rows.Add("30 mins", TimeSpan.FromMinutes(30))
            .Rows.Add("1 hr", TimeSpan.FromHours(1))
            .Rows.Add("3 hrs", TimeSpan.FromHours(3))
            .Rows.Add("6 hrs", TimeSpan.FromHours(6))
        End With
        Return table
    End Function
    ''' <summary>
    ''' Handles the SelectedIndexChanged event for ComboBox1.
    ''' Updates the application settings and applies an age filter to the DataGridView
    ''' based on the selected value in ComboBox1.
    ''' </summary>
    ''' <param name="sender">The ComboBox control that triggered the event.</param>
    ''' <param name="e">Provides data for the event.</param>
    ''' <remarks>
    ''' Key functionality:
    ''' 1. Saves the selected value from ComboBox1 to the application settings (My.Settings.Age).
    ''' 2. Calls the ApplyAgeFilter method to filter the DataGridView rows based on the selected age.
    ''' This ensures that only rows within the specified age range are displayed.
    ''' </remarks>
    Private Sub ComboBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox1.SelectedIndexChanged
        My.Settings.Age = CType(sender, ComboBox).Text ' Save the selected value to settings
        My.Settings.Save()
        ApplyAgeFilter(sender)
    End Sub
    ''' <summary>
    ''' Handles the SelectedIndexChanged event for ComboBox2.
    ''' Updates the application settings and adjusts the polling interval for the DX cluster server
    ''' based on the selected value in ComboBox2.
    ''' </summary>
    ''' <param name="sender">The ComboBox control that triggered the event.</param>
    ''' <param name="e">Provides data for the event.</param>
    ''' <remarks>
    ''' Key functionality:
    ''' 1. Saves the selected value from ComboBox2 to the application settings (My.Settings.Update).
    ''' 2. Calls the ApplyUpdateInterval method to update the PollingTimer's interval and trigger an immediate poll.
    ''' This ensures that the polling interval is dynamically adjusted based on the user's selection.
    ''' </remarks>
    Private Sub ComboBox2_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox2.SelectedIndexChanged
        My.Settings.Update = CType(sender, ComboBox).Text ' Save the selected value to settings
        My.Settings.Save()
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
    Public Sub ApplyUpdateInterval(sender As Object)
        ' Ensure cross-thread protection
        If ComboBox2.InvokeRequired Then
            ComboBox2.Invoke(New Action(Of Object)(AddressOf ApplyUpdateInterval), sender)
            Return
        End If

        InvokeIfRequired(ComboBox2, AddressOf SetPollingInterval)
        PollCluster()       ' poll cluster when update interval is changed
    End Sub
#End Region

#Region "Event Handlers"
    ' Methods related to Events
#End Region

#Region "Initialization"
    ' Methods related to Initialization

    ''' <summary>
    ''' Handles the Load event for the frmCluster form.
    ''' Initializes the form's controls, sets up the DataGridView, configures ComboBoxes, 
    ''' dynamically adds checkboxes for amateur bands, and establishes a connection to the DX cluster server.
    ''' </summary>
    ''' <param name="sender">The source of the event, typically the frmCluster form.</param>
    ''' <param name="e">The EventArgs containing event data.</param>
    ''' <remarks>
    ''' This method performs the following actions:
    ''' - Configures the DataGridView (DataGridView1) with a data source, column visibility, sorting, and display order.
    ''' - Retrieves and applies amateur band settings to dynamically created checkboxes.
    ''' - Configures ComboBoxes (ComboBox1 and ComboBox2) for age filtering and update intervals.
    ''' - Establishes a TCP connection to the DX cluster server and starts a BackgroundWorker for asynchronous communication.
    ''' - Ensures the user is logged in before proceeding with further operations.
    ''' - Sets up a polling timer to periodically poll the DX cluster server.
    ''' </remarks>
    Private Async Sub frmCluster_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Create data source

        Dim DisplayOrder() As String = {"CC11", "DX Call", "Frequency", "Band", "Mode", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid"}    ' the order the columns are displayed
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
            ' Sort the DataTable by Date and Time
            .ColumnHeadersDefaultCellStyle.Font = New Font(DataGridView1.Font, FontStyle.Bold) ' Make the header row bold
            .Columns("Date").DefaultCellStyle.Format = "dd-MMM"
            ' Apply the display order
            If DisplayOrder.Length <> .DataSource.columns.count Then Throw New Exception("There needs to be 1 entry in DisplayOrder for every column in the datasource")
            For i As Integer = 0 To DisplayOrder.Length - 1
                .Columns(DisplayOrder(i)).DisplayIndex = i
            Next
            .Refresh()
        End With

        OpenWSIDialogs = GetOpenWSIDialogs() ' get list of open WSI dialogs

        PollingTimer = New Timers.Timer With {
            .AutoReset = True ' Ensure the timer repeats
            }
        AddHandler PollingTimer.Elapsed, AddressOf OnPollingTimerElapsed

        ' Temporarily remove the event handlers
        RemoveHandler ComboBox1.SelectedIndexChanged, AddressOf ComboBox1_SelectedIndexChanged
        RemoveHandler ComboBox2.SelectedIndexChanged, AddressOf ComboBox2_SelectedIndexChanged
        ' Create the Age drop down
        BindTimeSpanComboBox(ComboBox1, AddressOf GetAgeSpanDataSource)
        ' Set the default value for ComboBox1
        Dim valueToSelect As String = My.Settings.Age
        Dim Index As Integer = ComboBox1.FindStringExact(valueToSelect)
        If Index >= 0 Then
            ComboBox1.SelectedIndex = Index
        Else
            Throw New Exception($"Value '{valueToSelect}' not found in ComboBox1.")
        End If

        ' Create the Update drop down
        BindTimeSpanComboBox(ComboBox2, AddressOf GetUpdateSpanDataSource)
        ' Set the default value for ComboBox2
        valueToSelect = My.Settings.Update
        Index = ComboBox2.FindStringExact(valueToSelect)
        If Index >= 0 Then
            ComboBox2.SelectedIndex = Index
        Else
            Throw New Exception($"Value '{valueToSelect}' not found in ComboBox2.")
        End If
        ' Reattach the event handlers
        AddHandler ComboBox1.SelectedIndexChanged, AddressOf ComboBox1_SelectedIndexChanged
        AddHandler ComboBox2.SelectedIndexChanged, AddressOf ComboBox2_SelectedIndexChanged

        AddBandCheckboxes()
        Dim SelectedBands = GetAmateurBandSettings() ' get the amateur band settings
        Dim groupBox = Me.Controls.OfType(Of GroupBox)().FirstOrDefault(Function(g) g.Text = AmateurBandsGroupBoxText)
        ' Set the Checked property of each CheckBox based on the selected bands
        For Each checkBox In groupBox.Controls.OfType(Of CheckBox)()
            checkBox.Checked = SelectedBands.Contains(checkBox.Name) ' check the boxes for the selected bands
        Next

        LoadSounds()    ' load list of sound files

        ' Connect to the DX cluster server
        Await InitializeCluster()

        ' Wait until the user is logged in
        While Not LoggedIn
            Await Task.Delay(100) ' Check every 100ms
        End While
    End Sub
    ''' <summary>
    ''' Loads the available alert sounds into ComboBox3 and sets the selected alert sound.
    ''' </summary>
    ''' <remarks>
    ''' This method performs the following actions:
    ''' 1. Retrieves a list of available `.wav` files from the sound directory using the `SoundPlayerHelper`.
    ''' 2. Populates `ComboBox3` with the list of sound files.
    ''' 3. Temporarily removes the `SelectedIndexChanged` event handler to prevent unnecessary event triggers during initialization.
    ''' 4. Sets the selected item in `ComboBox3` to the alert sound saved in `My.Settings.Alert`.
    ''' 5. If the saved alert sound is not found, defaults to the first entry in the list and displays a message to the user.
    ''' 6. Reattaches the `SelectedIndexChanged` event handler after initialization.
    ''' 
    ''' This method ensures that the alert sound selection is properly initialized and prevents unnecessary event handling during setup.
    ''' </remarks>
    Sub LoadSounds()
        If ComboBox3.InvokeRequired Then
            ComboBox3.Invoke(New Action(AddressOf LoadSounds))
            Return
        End If
        ' Load alert sounds
        Dim sounds As List(Of String) = soundPlayer.GetWavFiles()
        ComboBox3.DataSource = sounds
        ' Temporarily remove the event handler
        RemoveHandler ComboBox3.SelectedIndexChanged, AddressOf ComboBox3_SelectedIndexChanged
        ' Update the DataSource

        Dim alertSound As String = My.Settings.Alert
        Dim alertIndex As Integer = ComboBox3.FindStringExact(alertSound)    ' select current alert
        If alertIndex >= 0 Then
            ComboBox3.SelectedIndex = alertIndex ' Select the matching entry
        Else
            MessageBox.Show($"Alert sound '{alertSound}' not found in the list. Defaulting to the first entry.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information)
            ComboBox3.SelectedIndex = 0 ' Default to the first entry if no match is found
        End If
        ' Reattach the event handler
        AddHandler ComboBox3.SelectedIndexChanged, AddressOf ComboBox3_SelectedIndexChanged
    End Sub
    ''' <summary>
    ''' Dynamically adds a group of checkboxes to the form, one for each amateur radio band.
    ''' </summary>
    ''' <remarks>
    ''' - A GroupBox is created to visually group the checkboxes.
    ''' - Each checkbox represents an amateur band, with its text set to the band name.
    ''' - The checkboxes are arranged vertically within the GroupBox.
    ''' - The GroupBox is added to the form at a specified location.
    ''' - The method uses the distinct values from the FrequencyBands dictionary to generate the checkboxes.
    ''' </remarks>
    Private Sub AddBandCheckboxes()
        ' Create a GroupBox to hold the checkboxes
        ' Calculate location for groupbox, just below the Update interval
        Dim location = Me.ComboBox2.Location
        location.Y += 40
        ' Create groupbox
        Dim groupBox As New GroupBox With {
        .Text = AmateurBandsGroupBoxText,
        .Location = location,
        .AutoSize = True,
        .Margin = New Padding(10)
    }

        ' Add the GroupBox to the form
        Me.Controls.Add(groupBox)

        ' Dynamically create checkboxes for each band
        Dim xOffset As Integer = 10 ' Initial horizontal offset
        Dim yOffset As Integer = 20 ' Initial vertical offset
        Dim columnWidth As Integer = 60 ' Width of each column
        Dim maxRows As Integer = Math.Ceiling(FrequencyBands.Values.Distinct().Count() / 2.0) ' Max rows per column
        Dim currentRow As Integer = 0 ' Track the current row

        For Each band In FrequencyBands.Values.Distinct()
            ' Create a new checkbox
            Dim checkBox As New CheckBox With {
            .Text = band,
            .Name = band, ' Set the checkbox's name to the band name for easy identification
            .Location = New Point(xOffset, yOffset),
            .AutoSize = True,
            .Checked = True
        }

            ' Attach the CheckedChanged event to save settings
            AddHandler checkBox.CheckedChanged, Sub(sender, e) UpdateDataGridViewFilter()

            ' Add the checkbox to the GroupBox
            groupBox.Controls.Add(checkBox)

            ' Update the position for the next checkbox
            currentRow += 1
            If currentRow >= maxRows Then
                ' Move to the next column
                currentRow = 0
                xOffset += columnWidth
                yOffset = 20 ' Reset vertical offset for the new column
            Else
                ' Move to the next row in the current column
                yOffset += 25
            End If
        Next
    End Sub
    ''' <summary>
    ''' Reads the amateur band settings from My.Settings and returns a list of selected bands.
    ''' </summary>
    ''' <returns>A list of strings representing the selected amateur bands.</returns>
    Private Function GetAmateurBandSettings() As List(Of String)
        ' Check if the setting exists and is not empty
        If Not String.IsNullOrEmpty(My.Settings.AmateurBands) Then
            ' Split the comma-separated string into a list of bands
            Return My.Settings.AmateurBands.Split(","c).Select(Function(b) b.Trim()).ToList()
        End If

        ' Return an empty list if no settings are found
        Return New List(Of String)()
    End Function

    Private Sub btnClose_Click(sender As Object, e As EventArgs) Handles btnClose.Click
        Me.Close() ' Closes the form
    End Sub
    ''' <summary>
    ''' Handles the Closing event for the frmCluster form.
    ''' Ensures that the ClusterManager is properly disposed of.
    ''' </summary>
    Private Sub frmCluster_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        ' Dispose of the ClusterManager
        clusterManager?.Dispose()

        ' Stop the polling timer
        If PollingTimer IsNot Nothing Then
            PollingTimer.Stop()
            PollingTimer.Dispose()
        End If

        Debug.WriteLine("frmCluster: Resources cleaned up on closing.")
    End Sub
    Private Sub ComboBox3_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox3.SelectedIndexChanged
        InvokeIfRequired(ComboBox3, Sub()
                                        My.Settings.Alert = ComboBox3.SelectedItem.ToString() ' Save the selected value to settings
                                        My.Settings.Save()
                                        soundPlayer.PlayWavWithLimit(My.Settings.Alert, 2 * 1000) ' Plays the selected alert sound
                                    End Sub)
    End Sub
#End Region
End Class
Public Class ClusterManager
    Private Const ClusterHost As String = "hrd.wa9pie.net"
    Private Const ClusterPort As Integer = 8000
    Private Const LoginTimeout As Integer = 60 ' Timeout for login in seconds
    Private Const CommandTimeout As Integer = 20 ' Timeout for commands in seconds
    Private Const MaxRetries As Integer = 3 ' Maximum retries for login

    Private ReadOnly ClusterClient As New TcpClient()
    Private ReadOnly Buffer As New StringBuilder()

    ''' <summary>
    ''' Connects to the cluster and waits for the "login: " prompt.
    ''' Retries up to MaxRetries times if the prompt is not received within the timeout.
    ''' </summary>
    ''' <returns>True if connected and "login: " received, False otherwise.</returns>
    Public Async Function ConnectAsync() As Task(Of Boolean)
        For attempt As Integer = 1 To MaxRetries
            Try
                Await ClusterClient.ConnectAsync(ClusterHost, ClusterPort)
                If Await WaitForResponseAsync("login: ", LoginTimeout) Then
                    Return True
                End If
            Catch ex As Exception
                Debug.WriteLine($"Connection attempt {attempt} failed: {ex.Message}")
            End Try

            ' Retry logic
            If attempt < MaxRetries Then
                Debug.WriteLine($"Retrying connection... ({attempt}/{MaxRetries})")
                Await Task.Delay(1000) ' Wait 1 second before retrying
            End If
        Next

        Debug.WriteLine("Failed to connect to the cluster after maximum retries.")
        Return False
    End Function

    ''' <summary>
    ''' Sends a command to the DX cluster server and waits for a specific response.
    ''' </summary>
    ''' <param name="command">The command to send to the server.</param>
    ''' <param name="EndOfCommand">The expected string that indicates the end of the server's response.</param>
    ''' <returns>
    ''' A task that represents the asynchronous operation. The task result contains the server's response as a string 
    ''' if the expected response is received within the timeout; otherwise, an empty string.
    ''' </returns>
    ''' <remarks>
    ''' This method sends a command to the server using a TCP connection and waits for the server's response.
    ''' It ensures that the command is sent and the response is received within the specified timeout.
    ''' If the response does not match the expected end string, an empty string is returned.
    ''' </remarks>
    ''' <exception cref="InvalidOperationException">Thrown if the command data is empty.</exception>
    ''' <example>
    ''' Example usage:
    ''' <code>
    ''' Dim response As String = Await clusterManager.SendCommandAsync("sh/dx", ">>")
    ''' If Not String.IsNullOrEmpty(response) Then
    '''     Console.WriteLine("Response received: " & response)
    ''' Else
    '''     Console.WriteLine("No response or timeout occurred.")
    ''' End If
    ''' </code>
    ''' </example>
    Public Async Function SendCommandAsync(command As String, Optional isMultiline As Boolean = False) As Task(Of String)
        Try
            ' Send the command
            Dim data As Byte() = Encoding.ASCII.GetBytes(command)
            ' Ensure the length is valid
            If data.Length > 0 Then
                Await ClusterClient.GetStream().WriteAsync(data, 0, data.Length)
                AppendTextSafe(frmCluster.TextBox1, command) ' Append the command to the TextBox
                Debug.WriteLine($"Command sent: {command}")
            Else
                Throw New InvalidOperationException("Command data is empty.")
            End If
            Await Task.Delay(500)   ' give cluster time to respond

            ' Wait for the response based on whether multiline is expected
            If isMultiline Then
                Return Await WaitForMultiLineResponseAsync()
            Else
                If Await WaitForResponseAsync(vbCrLf, CommandTimeout) Then
                    Return Buffer.ToString()
                End If
            End If

        Catch ex As Exception
            Debug.WriteLine($"Error sending command: {ex.Message}")
        End Try

        Return String.Empty
    End Function

    ''' <summary>
    ''' Waits for a specific response from the cluster within the given timeout.
    ''' </summary>
    ''' <param name="expectedResponse">The expected response string.</param>
    ''' <param name="timeoutInSeconds">The timeout in seconds.</param>
    ''' <returns>True if the response is received, False otherwise.</returns>
    Private Async Function WaitForResponseAsync(expectedResponse As String, timeoutInSeconds As Integer) As Task(Of Boolean)
        Debug.WriteLine($"Waiting for response: {expectedResponse}")
        Dim timeout As TimeSpan = TimeSpan.FromSeconds(timeoutInSeconds)
        Dim startTime As DateTime = DateTime.Now

        While DateTime.Now - startTime < timeout
            If ClusterClient.Available > 0 Then
                Dim byteBuffer(ClusterClient.Available - 1) As Byte
                Dim bytesRead As Integer = Await ClusterClient.GetStream().ReadAsync(byteBuffer, 0, byteBuffer.Length)

                ' Append the data to the StringBuilder
                If bytesRead > 0 Then
                    Dim receivedData = Encoding.ASCII.GetString(byteBuffer, 0, bytesRead)
                    Buffer.Append(receivedData)
                    AppendTextSafe(frmCluster.TextBox1, receivedData)
                    Debug.WriteLine($"Received data: {receivedData}")

                    ' Check if the expected response is received
                    If Buffer.ToString().EndsWith(expectedResponse) Then
                        Debug.WriteLine($"Expected response received: {Buffer}")
                        Return True
                    End If
                End If
            End If

            Await Task.Delay(100) ' Check every 100ms
        End While

        Debug.WriteLine($"Timeout waiting for response: {expectedResponse}")
        Return False
    End Function

    ''' <summary>
    ''' Reads a multi-line response from the TCP stream until a period of inactivity is detected.
    ''' </summary>
    ''' <returns>
    ''' A task that represents the asynchronous operation. The task result contains the complete 
    ''' multi-line response as a single string.
    ''' </returns>
    ''' <remarks>
    ''' This method continuously reads data from the TCP stream in chunks and appends it to a 
    ''' <see cref="StringBuilder"/>. The method stops reading when no data is received for a 
    ''' specified timeout period (1 second of inactivity).
    ''' 
    ''' Key features:
    ''' - Handles multi-line responses from the server.
    ''' - Uses a timeout to detect the end of the response.
    ''' - Updates the UI with received data using <see cref="AppendTextSafe"/>.
    ''' - Logs received data and the final response for debugging purposes.
    ''' </remarks>
    ''' <example>
    ''' Example usage:
    ''' <code>
    ''' Dim response As String = Await WaitForMultiLineResponseAsync()
    ''' If Not String.IsNullOrEmpty(response) Then
    '''     Console.WriteLine("Response received: " & response)
    ''' Else
    '''     Console.WriteLine("No response or timeout occurred.")
    ''' End If
    ''' </code>
    ''' </example>
    Private Async Function WaitForMultiLineResponseAsync() As Task(Of String)
        Dim timeout As TimeSpan = TimeSpan.FromSeconds(5) ' Timeout for inactivity
        Dim startTime As DateTime = DateTime.Now
        Dim responseBuilder As New StringBuilder()

        Debug.WriteLine("Waiting for multi-line response")
        Debug.Flush()

        While True
            ' Check if data is available
            If ClusterClient.Available > 0 Then
                Dim byteBuffer(ClusterClient.Available - 1) As Byte
                Dim bytesRead As Integer = Await ClusterClient.GetStream().ReadAsync(byteBuffer, 0, byteBuffer.Length)

                If bytesRead > 0 Then
                    Dim receivedData = Encoding.ASCII.GetString(byteBuffer, 0, bytesRead)
                    responseBuilder.Append(receivedData)
                    AppendTextSafe(frmCluster.TextBox1, receivedData)
                    Debug.WriteLine($"Received data chunk: {receivedData}")

                    ' Reset the timeout since data was received
                    startTime = DateTime.Now
                End If
            Else
                ' Check for inactivity timeout
                If DateTime.Now - startTime > timeout Then
                    Debug.WriteLine("No more data received. Ending response collection.")
                    Exit While
                End If

                Await Task.Delay(100) ' Wait briefly before checking again
            End If
        End While

        Dim finalResponse = responseBuilder.ToString()
        Return finalResponse
    End Function


    ''' <summary>
    ''' Disposes of the ClusterManager resources, including closing the TCP connection.
    ''' </summary>
    Public Sub Dispose()
        Try
            If ClusterClient IsNot Nothing AndAlso ClusterClient.Connected Then
                ClusterClient.Close()
                Debug.WriteLine("ClusterManager: TCP connection closed.")
            End If
        Catch ex As Exception
            Debug.WriteLine($"ClusterManager: Error during Dispose - {ex.Message}")
        End Try
    End Sub
End Class

