Imports System.Net.Sockets
Imports System.ComponentModel
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar
Imports System.Text
Imports System.Collections.Concurrent
Imports System.Threading

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
    Private soundPlayer As New SoundPlayerHelper()      ' Load the sound player
    Private clusterManager As New ClusterManager()
    ' As the forms controls are not directly accessible, we need to use FindControlRecursive to get them
    Private dgv1 As DataGridView, cmb1 As ComboBox, cmb2 As ComboBox, cmb3 As ComboBox, gb1 As GroupBox, txt1 As TextBox, txt2 As TextBox

    ''' <summary>
    ''' Saves the selected amateur bands to My.Settings whenever a checkbox is changed.
    ''' </summary>
    Private Sub SaveAmateurBands()
        ' Get all checked checkboxes in the GroupBox
        Dim selectedBands = gb1.Controls.OfType(Of CheckBox)().
                            Where(Function(cb) cb.Checked).
                            Select(Function(cb) cb.Text)

        ' Save the selected bands as a comma-separated string in My.Settings
        My.Settings.AmateurBands = String.Join(",", selectedBands)
        My.Settings.Save()
    End Sub

#Region "Initialization"
    ''' <summary>
    ''' Initializes the DX cluster with predefined settings and starts the polling timer.
    ''' This method ensures that the cluster is configured only once and prevents multiple initializations.
    ''' </summary>
    ''' <returns>A Task representing the asynchronous operation.</returns>
    Private Async Function InitializeCluster() As Task
        ' Create a CancellationTokenSource
        ' Connect to the cluster
        If ClusterInitialized Then Return ' Prevent multiple initializations
        Await clusterManager.ConnectAsync() ' Pass the CancellationToken
        Await SendClusterCommand($"{ClusterUsername}{vbCrLf}") ' Send username
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
        InvokeIfRequired(cmb2, AddressOf SetPollingInterval)

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
    End Function

    ''' <summary>
    ''' Sets the PollingTimer interval based on the selected value in ComboBox2.
    ''' Ensures thread-safe access and validates the selected value.
    ''' </summary>
    Private Sub SetPollingInterval()
        ' Validate and set the interval
        If cmb2.SelectedValue IsNot Nothing AndAlso TypeOf cmb2.SelectedValue Is TimeSpan Then
            Dim selectedTimeSpan As TimeSpan = CType(cmb2.SelectedValue, TimeSpan)
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


        Dim callsigns = GetAllTabTags()     ' Get list of all tabs

        ' Perform polling logic
        For Each item In callsigns
            ' Send the sh/dx command and wait for a burst of data
            Dim response = Await clusterManager.SendCommandAsync($"sh/dx {item}{vbCrLf}", isMultiline:=True)
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
        ApplyAgeFilter(cmb1)  ' Apply the age filter
    End Sub
    ''' <summary>
    ''' Retrieves a list of all <see cref="TabPage.Tag"/> values from the TabControl in the main form.
    ''' </summary>
    ''' <returns>
    ''' A <see cref="List(Of Object)"/> containing the <see cref="TabPage.Tag"/> values of all tabs in the TabControl.
    ''' </returns>
    Private Function GetAllTabTags() As List(Of Object)
        Dim tags As New List(Of Object)()

        ' Iterate through all TabPages in TabControl1
        For Each tab As TabPage In Form1.TabControl1.TabPages
            If tab.Tag IsNot Nothing Then
                tags.Add(tab.Tag) ' Add the Tag value to the list
            End If
        Next

        Return tags
    End Function
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
        ' Get the list of checked bands
        Dim checkedBands = gb1.Controls.OfType(Of CheckBox)().
                       Where(Function(cb) cb.Checked).
                       Select(Function(cb) cb.Text).
                       ToList()
        ' Get the DataTable from the DataGridView's DataSource
        Dim dt As DataTable = GetDataTableFromDataGridView(dgv1)
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
        ' Get the current row
        Dim row As DataGridViewRow = dgv1.Rows(e.RowIndex)
        Dim callsign = row.Cells("DX Call").Value
        ' get tab containing data
        Dim tab = GetTabPageByTag(callsign)
        If tab Is Nothing Then Return
        ' Get the band value from the current row
        Dim band As String = row.Cells("Band").Value?.ToString()
        If String.IsNullOrEmpty(band) Then Return ' Exit if the band value is empty

        Dim dgv = CType(tab.Controls.Find($"DataGridView1_{callsign}", True).FirstOrDefault(), DataGridView) ' find DataGridView1 in WSI dialog
        If dgv.DataSource IsNot Nothing Then
            ' Get the DataTable from wsi form DataGridView1.DataSource
            Dim dt As DataTable = TryCast(dgv.DataSource, DataTable)

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
    End Sub
    ''' <summary>
    ''' Retrieves a reference to a TabPage in the TabControl with the specified Tag value.
    ''' </summary>
    ''' <param name="tagValue">The Tag value to search for.</param>
    ''' <returns>
    ''' The <see cref="TabPage"/> with the specified Tag value, or <c>Nothing</c> if no match is found.
    ''' </returns>
    Private Function GetTabPageByTag(tagValue As Object) As TabPage
        ' Iterate through all TabPages in TabControl1
        For Each tab As TabPage In Form1.TabControl1.TabPages
            If tab.Tag IsNot Nothing AndAlso tab.Tag.Equals(tagValue) Then
                Return tab ' Return the matching TabPage
            End If
        Next

        ' Return Nothing if no match is found
        Return Nothing
    End Function

    ''' <summary>
    ''' Handles the CellMouseEnter event for DataGridView1.
    ''' Highlights the cell in the "Band" column of the hovered row by setting its background color to yellow.
    ''' </summary>
    ''' <param name="sender">The DataGridView control that triggered the event.</param>
    ''' <param name="e">Provides data for the CellMouseEnter event, including the row and column indices of the hovered cell.</param>
    ''' <remarks>
    ''' This method ensures that only the cell in the "Band" column of the hovered row is highlighted.
    ''' It checks if the hovered cell belongs to the "Band" column before applying the highlight.
    ''' </remarks>

    Private Sub DataGridView1_CellMouseEnter(sender As Object, e As DataGridViewCellEventArgs) Handles DataGridView1.CellMouseEnter
        FormatHighlight(sender, e, Color.Yellow)
    End Sub

    Private Sub DataGridView1_CellMouseLeave(sender As Object, e As DataGridViewCellEventArgs) Handles DataGridView1.CellMouseLeave
        ' Ensure the mouse is over a valid row and column
        FormatHighlight(sender, e, Color.Empty)     ' original background
    End Sub

    Dim HighlightedColumns As New List(Of String) From {"DX Call", "Frequency", "Band", "Mode", "Spotter"}     ' list of columns with highlighting
    ''' <summary>
    ''' Highlights or resets the background color of cells in a DataGridView column 
    ''' based on matching values in the specified column and the "DX Call" column.
    ''' </summary>
    ''' <param name="sender">The DataGridView control that triggered the event.</param>
    ''' <param name="e">Provides data for the DataGridView cell event, including the row and column indices of the cell.</param>
    ''' <param name="color">The color to apply to the matching cells. Use Color.White to reset the background color.</param>
    ''' <remarks>
    ''' This method checks if the hovered cell belongs to a highlighted column (e.g., "DX Call", "Band", or "Spotter").
    ''' If it does, it highlights all cells in the same column and row that match the hovered cell's value and "DX Call".
    ''' </remarks>

    Private Sub FormatHighlight(sender As Object, e As DataGridViewCellEventArgs, color As Color)
        If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 Then
            Dim dgv As DataGridView = CType(sender, DataGridView)
            If HighlightedColumns.Contains(dgv.Columns(e.ColumnIndex).Name) Then    ' it's a highlighted column
                Dim currentRow As DataGridViewRow = dgv.Rows(e.RowIndex)
                ' Get the Band and DX Call values of the current row
                Dim dxCall As String = currentRow.Cells("DX Call").Value?.ToString()
                Dim HighlightedCell As String = currentRow.Cells(e.ColumnIndex).Value?.ToString()

                ' Highlight all cells in the same Band and DX Call
                For Each row As DataGridViewRow In dgv.Rows
                    If row.Cells("DX Call").Value?.ToString() = dxCall AndAlso row.Cells(e.ColumnIndex).Value?.ToString() = HighlightedCell Then
                        row.Cells(e.ColumnIndex).Style.BackColor = color
                    End If
                Next
            End If
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
        If dgv1.InvokeRequired Then
            dgv1.Invoke(New Action(Of String)(AddressOf ParseClusterData), data)
        Else
            data = data.Trim(vbCr, vbLf)    ' remove rubbish
            Dim columns As String() = data.Split("^"c) ' Split the string into columns
            If columns(0) = "CC11" Then     ' it's a cluster message
                Dim dt As DataTable
                If TypeOf dgv1.DataSource Is DataTable Then
                    dt = CType(dgv1.DataSource, DataTable)
                ElseIf TypeOf dgv1.DataSource Is DataView Then
                    dt = CType(CType(dgv1.DataSource, DataView).Table, DataTable)
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
                Dim rows As DataRow() = dt.Select($"Frequency='{row("Frequency")}' AND [DX Call]='{row("DX Call")}' AND Date='{row("Date")}' AND Time='{row("Time").tolower}' AND Spotter='{row("Spotter")}'")
                If rows.Length = 0 Then     ' it does not, so add
                    ' Check that the callsign is in the tab list. Sometimes some unasked for ones are present
                    If GetTabPageByTag(row("DX Call")) IsNot Nothing Then
                        row("Band") = FreqToBand(CSng(row("Frequency")))      ' create band column
                        row("Mode") = InferMode(CSng(row("Frequency")))      ' create mode column")
                        row("Time") = row("Time").tolower()           ' 
                        dt.Rows.Add(row) ' Add the new row to the DataTable
                    End If
                End If
                ResizeForm() ' Resize the form to fit the new data
                dgv1.Refresh() ' Refresh the DataGridView to show the new data
            End If
        End If
    End Sub
    ''' <summary>
    ''' Dynamically resizes the form to fit the combined dimensions of the TableLayoutPanel and DataGridView.
    ''' </summary>
    ''' <remarks>
    ''' This method calculates the total height and width of the rows and columns in the TableLayoutPanel,
    ''' adds the dimensions of the DataGridView, and adjusts the form's size accordingly.
    ''' It ensures that the form accommodates all its child controls without clipping.
    ''' </remarks>
    Sub ResizeForm()
        With dgv1
            .AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells)
            .AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells)
            .Refresh()
        End With
        Dim height = TableLayoutPanel1.GetRowHeights().Sum() + SystemInformation.CaptionHeight '+ DataGridView1.Height
        Dim width = TableLayoutPanel1.GetColumnWidths().Sum() '+ DataGridView1.Width
        Dim newSize = New Size(width, height)
        Me.Size = newSize
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
        Dim dt As DataTable = GetDataTableFromDataGridView(dgv1)

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
            ResizeForm() ' Resize the form to fit the new data
            dgv1.Refresh()
        End If
    End Sub

    ''' <summary>
    ''' Determines if a DataTable row is considered "old" based on the selected TimeSpan from ComboBox1.
    ''' </summary>
    ''' <param name="row">The DataTable row to evaluate.</param>
    ''' <returns>True if the row is older than the cutoff DateTime; otherwise, False.</returns>
    Private Function SpotIsOld(row As DataRow) As Boolean
        Try
            ' Validate ComboBox1's selected value
            Dim selectedTimeSpan As TimeSpan
            If cmb1.SelectedValue IsNot Nothing Then
                If TypeOf cmb1.SelectedValue Is TimeSpan Then
                    selectedTimeSpan = CType(cmb1.SelectedValue, TimeSpan)
                ElseIf TypeOf cmb1.SelectedItem Is DataRowView Then
                    Dim rowView As DataRowView = CType(cmb1.SelectedItem, DataRowView)
                    selectedTimeSpan = CType(rowView("TimeSpanValue"), TimeSpan)
                Else
                    Debug.WriteLine("Error: Unable to extract TimeSpan from ComboBox1.SelectedValue.")
                    Return False
                End If
            Else
                Debug.WriteLine("Error: ComboBox1.SelectedValue is null.")
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
                If Integer.TryParse(timeString.AsSpan(0, 2), Nothing) AndAlso
               Integer.TryParse(timeString.AsSpan(2, 2), Nothing) Then
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
            .Rows.Add("12 hrs", TimeSpan.FromHours(12))
            .Rows.Add("24 hrs", TimeSpan.FromHours(24))
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
        If cmb2.InvokeRequired Then
            cmb2.Invoke(New Action(Of Object)(AddressOf ApplyUpdateInterval), sender)
            Return
        End If

        InvokeIfRequired(cmb2, AddressOf SetPollingInterval)
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
        ' Find controls
        Try
            dgv1 = CType(DataGridView1, DataGridView)
            cmb1 = CType(ComboBox1, ComboBox)
            cmb2 = CType(ComboBox2, ComboBox)
            cmb3 = CType(ComboBox3, ComboBox)
            gb1 = CType(GroupBox1, GroupBox)
            txt1 = CType(TextBox1, TextBox)
        Catch ex As Exception
            Debug.WriteLine($"Error finding controls: {ex.Message}")
            Return
        End Try
        TableLayoutPanel1.RowStyles(0).SizeType = SizeType.Percent
        TableLayoutPanel1.RowStyles(0).Height = 100 ' Allocate 100% of the height to the row
        TableLayoutPanel1.ColumnStyles(0).SizeType = SizeType.Percent
        TableLayoutPanel1.ColumnStyles(0).Width = 100 ' Allocate 100% of the width to the column
        DataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        DataGridView1.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        TableLayoutPanel1.PerformLayout()

        Dim DisplayOrder() As String = {"CC11", "DX Call", "Frequency", "Band", "Mode", "Date", "Time", "Comment", "Spotter", "Entity", "Spotter DXCC", "Spotter Node", "ITU DX", "CQ DX", "ITU Spotter", "CQ Spotter", "DX State", "Spotter State", "DX Country", "Spotter Country", "DX Grid", "Spotter Grid"}    ' the order the columns are displayed

        With dgv1
            .AutoGenerateColumns = True
            .RowHeadersVisible = False ' Hide the row headers if not needed
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells ' Auto-size columns to fit content
            .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells ' Auto-size rows to fit content
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize ' Auto-size column headers
            .RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders ' Auto-size row headers
            .DataSource = GetDataSource() ' Bind the data source
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
            .Sort(dgv1.Columns("Date"), ListSortDirection.Descending)
            .Sort(dgv1.Columns("Time"), ListSortDirection.Descending)
            ' Sort the DataTable by Date and Time
            .ColumnHeadersDefaultCellStyle.Font = New Font(dgv1.Font, FontStyle.Bold) ' Make the header row bold
            .Columns("Date").DefaultCellStyle.Format = "dd-MMM"
            ' Apply the display order
            If DisplayOrder.Length <> .DataSource.columns.count Then Throw New Exception("There needs to be 1 entry in DisplayOrder for every column in the datasource")
            For i As Integer = 0 To DisplayOrder.Length - 1
                .Columns(DisplayOrder(i)).DisplayIndex = i
            Next

            ' Ensure TableLayoutPanel allows resizing
            TableLayoutPanel1.RowStyles(1).SizeType = SizeType.AutoSize
            TableLayoutPanel1.ColumnStyles(0).SizeType = SizeType.AutoSize

            ' Force auto-resize after data binding
            For Each column As DataGridViewColumn In dgv1.Columns
                column.Width = -1 ' Reset to default
            Next
            dgv1.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells)
            dgv1.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells)
            .Refresh()
        End With

        PollingTimer = New Timers.Timer With {
            .AutoReset = True ' Ensure the timer repeats
            }
        AddHandler PollingTimer.Elapsed, AddressOf OnPollingTimerElapsed

        ' Temporarily remove the event handlers
        RemoveHandler cmb1.SelectedIndexChanged, AddressOf ComboBox1_SelectedIndexChanged
        RemoveHandler cmb2.SelectedIndexChanged, AddressOf ComboBox2_SelectedIndexChanged
        ' Create the Age drop down
        BindTimeSpanComboBox(cmb1, AddressOf GetAgeSpanDataSource)
        ' Set the default value for ComboBox1
        Dim valueToSelect As String = My.Settings.Age
        Dim Index As Integer = cmb1.FindStringExact(valueToSelect)
        If Index >= 0 Then
            cmb1.SelectedIndex = Index
        Else
            Throw New Exception($"Value '{valueToSelect}' not found in ComboBox1.")
        End If

        ' Create the Update drop down
        BindTimeSpanComboBox(cmb2, AddressOf GetUpdateSpanDataSource)
        ' Set the default value for ComboBox2
        valueToSelect = My.Settings.Update
        Index = cmb2.FindStringExact(valueToSelect)
        If Index >= 0 Then
            cmb2.SelectedIndex = Index
        Else
            Throw New Exception($"Value '{valueToSelect}' not found in ComboBox2.")
        End If
        ' Reattach the event handlers
        AddHandler cmb1.SelectedIndexChanged, AddressOf ComboBox1_SelectedIndexChanged
        AddHandler cmb2.SelectedIndexChanged, AddressOf ComboBox2_SelectedIndexChanged

        AddBandCheckboxes()
        Dim SelectedBands = GetAmateurBandSettings() ' get the amateur band settings
        ' Set the Checked property of each CheckBox based on the selected bands
        For Each checkBox In gb1.Controls.OfType(Of CheckBox)()
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
        If cmb3.InvokeRequired Then
            cmb3.Invoke(New Action(AddressOf LoadSounds))
            Return
        End If
        ' Load alert sounds
        Dim sounds As List(Of String) = soundPlayer.GetWavFiles()

        ' Temporarily remove the event handler
        RemoveHandler cmb3.SelectedIndexChanged, AddressOf ComboBox3_SelectedIndexChanged

        ' Update the DataSource
        cmb3.DataSource = sounds

        Dim alertSound As String = My.Settings.Alert
        Dim alertIndex As Integer = cmb3.FindStringExact(alertSound)    ' select current alert
        If alertIndex >= 0 Then
            cmb3.SelectedIndex = alertIndex ' Select the matching entry
        Else
            MessageBox.Show($"Alert sound '{alertSound}' not found in the list. Defaulting to the first entry.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information)
            cmb3.SelectedIndex = 0 ' Default to the first entry if no match is found
        End If
        ' Reattach the event handler
        AddHandler cmb3.SelectedIndexChanged, AddressOf ComboBox3_SelectedIndexChanged
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
            GroupBox1.Controls.Add(checkBox)

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
        clusterManager?.Dispose()
        PollingTimer?.Dispose()
        Debug.WriteLine("Resources cleaned up on closing.")
    End Sub
    Private Sub ComboBox3_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox3.SelectedIndexChanged
        SaveSound(sender)
    End Sub
    Private Sub SaveSound(sender As Object)
        If sender.InvokeRequired Then
            sender.Invoke(New Action(Of Object)(AddressOf SaveSound))
        Else
            My.Settings.Alert = sender.SelectedItem.ToString() ' Save the selected value to settings
            My.Settings.Save()
            soundPlayer.PlayWavWithLimit(My.Settings.Alert, 2 * 1000) ' Plays the selected alert sound
        End If
    End Sub

    Private Sub DataGridView1_Resize(sender As Object, e As EventArgs) Handles DataGridView1.Resize
        Debug.WriteLine($"DataGridView1 resized to: {DataGridView1.Size}")
    End Sub

    Private Sub TableLayoutPanel1_Resize(sender As Object, e As EventArgs) Handles TableLayoutPanel1.Resize
        Debug.WriteLine($"TableLayoutPanel1 resized to: {TableLayoutPanel1.Size}")

    End Sub
#End Region
End Class