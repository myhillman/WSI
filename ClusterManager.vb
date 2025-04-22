Imports System.Net.Sockets
Imports System.Text
Imports System.Threading

Public Class ClusterManager
    Implements IDisposable

    ' Dispose pattern implementation
    Private disposedValue As Boolean
    Private Const ClusterHost As String = "hrd.wa9pie.net"
    Private Const ClusterPort As Integer = 8000
    Private Const LoginTimeout As Integer = 60 ' Timeout for login in seconds
    Private Const CommandTimeout As Integer = 20 ' Timeout for commands in seconds
    Private Const MaxRetries As Integer = 3 ' Maximum retries for login

    Private ReadOnly ClusterClient As New TcpClient()
    Private ReadOnly Buffer As New StringBuilder()

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                ' Dispose managed resources
                If ClusterClient IsNot Nothing AndAlso ClusterClient.Connected Then
                    ClusterClient.Close()
                End If
            End If
            ' Free unmanaged resources (if any)
            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub

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
    ''' Sends a command to the DX cluster server and optionally waits for a multi-line response.
    ''' </summary>
    ''' <param name="command">The command to send to the server.</param>
    ''' <param name="isMultiline">
    ''' A boolean indicating whether to wait for a multi-line response. 
    ''' If True, the method waits for a multi-line response; otherwise, it waits for a single-line response.
    ''' </param>
    ''' <returns>
    ''' A task that represents the asynchronous operation. The task result contains the server's response as a string 
    ''' if the response is received within the timeout; otherwise, an empty string.
    ''' </returns>
    ''' <remarks>
    ''' This method sends a command to the server using a TCP connection and waits for the server's response.
    ''' It ensures that the command is sent and the response is received within the specified timeout.
    ''' If the response does not match the expected format or timeout occurs, an empty string is returned.
    ''' </remarks>
    ''' <exception cref="InvalidOperationException">Thrown if the command data is empty.</exception>
    ''' <example>
    ''' Example usage:
    ''' <code>
    ''' Dim response As String = Await clusterManager.SendCommandAsync("sh/dx", True)
    ''' If Not String.IsNullOrEmpty(response) Then
    '''     Console.WriteLine("Response received: " &amp; response)
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
                AppendTextSafe(Form1.frmClusterInstance.TextBox1, command) ' Append the command to the TextBox
                Await ClusterClient.GetStream().WriteAsync(data)
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
        Dim localBuffer As New StringBuilder()
        Dim timeout As TimeSpan = TimeSpan.FromSeconds(timeoutInSeconds)
        Dim startTime As DateTime = DateTime.Now

        While DateTime.Now - startTime < timeout
            If ClusterClient.Available > 0 Then
                Dim byteBuffer(ClusterClient.Available - 1) As Byte
                Dim bytesRead As Integer = Await ClusterClient.GetStream().ReadAsync(byteBuffer)

                If bytesRead > 0 Then
                    Dim receivedData = Encoding.ASCII.GetString(byteBuffer, 0, bytesRead)
                    localBuffer.Append(receivedData)
                    AppendTextSafe(Form1.frmClusterInstance.TextBox1, receivedData)

                    If localBuffer.ToString().EndsWith(expectedResponse) Then
                        Return True
                    End If
                End If
            End If

            Await Task.Delay(100)
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
    '''     Console.WriteLine("Response received: " &amp; response)
    ''' Else
    '''     Console.WriteLine("No response or timeout occurred.")
    ''' End If
    ''' </code>
    ''' </example>
    Private Async Function WaitForMultiLineResponseAsync() As Task(Of String)
        Dim timeout As TimeSpan = TimeSpan.FromSeconds(5) ' Timeout for inactivity
        Dim startTime As DateTime = DateTime.Now
        Dim responseBuilder As New StringBuilder()

        While True
            ' Check if data is available
            If ClusterClient.Available > 0 Then
                Dim byteBuffer(ClusterClient.Available - 1) As Byte
                Dim bytesRead As Integer = Await ClusterClient.GetStream().ReadAsync(byteBuffer)

                If bytesRead > 0 Then
                    Dim receivedData = Encoding.ASCII.GetString(byteBuffer, 0, bytesRead)
                    responseBuilder.Append(receivedData)
                    AppendTextSafe(Form1.frmClusterInstance.TextBox1, receivedData)

                    ' Reset the timeout since data was received
                    startTime = DateTime.Now
                End If
            Else
                ' Check for inactivity timeout
                If DateTime.Now - startTime > timeout Then
                    Exit While
                End If
                Await Task.Delay(100) ' Wait briefly before checking again
            End If
        End While

        Return responseBuilder.ToString()
    End Function
End Class

