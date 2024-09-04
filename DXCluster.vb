Imports System.Net.Sockets
Imports System.Net
Imports System.Threading
Imports System.ComponentModel
Imports System.Timers
Imports System.ComponentModel.Design

Public Class frmCluster
    Const DXcluster = "hrd.wa9pie.net"
    Const port = 8000
    Dim Cluster As New TcpClient()
    Dim localAddr As IPAddress = IPAddress.Any
    Dim listener As New TcpListener(localAddr, port)
    Dim WithEvents bgw As New BackgroundWorker      ' use BackgroundWorker to poll tcp/ip port
    Dim LoggedIn As Boolean = False
    Dim ClusterBusy As Boolean = False
    Dim GetSpots As Boolean = False
    Enum BGWaction
        TextBox
        Send
        Logged
        Busy
    End Enum

    Private Sub frmCluster_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Cluster.Connect(DXcluster, port)        ' open the cluster port
        With bgw
            .WorkerSupportsCancellation = True
            .WorkerReportsProgress = True
            .RunWorkerAsync()           ' run the background worker
        End With
        While Not LoggedIn
            Application.DoEvents()
        End While
        Send_Sub($"set/name Marc{vbCrLf}")
        Send_Sub($"set/qth Donvale{vbCrLf}")
        Send_Sub($"set/qra QF22oe{vbCrLf}")
        Send_Sub($"unset/echo{vbCrLf}")
        Send_Sub($"set/prompt >>{vbCrLf}")
        Send_Sub($"set/ve7cc{vbCrLf}")
        Send_Sub($"unset/wcy{vbCrLf}")
        Send_Sub($"unset/wwv{vbCrLf}")
        Send_Sub($"sh/filter{vbCrLf}")
        Send_Sub($"clear/spots all")
        Send_Sub($"sh/filter{vbCrLf}")
        GetSpots = True
    End Sub
    Sub Send_Sub(ByVal msg As String)
        While ClusterBusy
            Thread.Sleep(100)
            Application.DoEvents()
        End While
        TextBox1.AppendText($"-> {msg}")
        Dim byt_to_send() As Byte = System.Text.Encoding.ASCII.GetBytes($"{msg}")
        Cluster.Client.Send(byt_to_send, 0, byt_to_send.Length, SocketFlags.None)
        ClusterBusy = True
        Application.DoEvents()
        Thread.Sleep(100)
    End Sub
    Private Sub bgw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles bgw.DoWork
        Dim command As String, buffer As String = "", cr As Integer
        Do
            If Cluster.Client.Available > 0 Then
                Dim byt_to_receive(Cluster.Available - 1) As Byte
                Cluster.Client.Receive(byt_to_receive, 0, byt_to_receive.Length, SocketFlags.None)
                Dim String_From_Byte As String = System.Text.Encoding.ASCII.GetString(byt_to_receive)
                buffer += String_From_Byte       ' add incoming characters to buffer
                If buffer.EndsWith("login: ") Then buffer += vbCrLf
                If buffer.EndsWith("password: ") Then buffer += vbCrLf
                cr = buffer.IndexOf(vbCr)
                While cr <> -1
                    If cr = 0 Then command = "" Else command = buffer.Substring(0, cr)    ' get command
                    buffer = buffer.Substring(cr + 2, buffer.Length - (cr + 2))   ' remove CRLF  from buffer
                    bgw.ReportProgress(BGWaction.TextBox, $"{command}{vbCrLf}")
                    If command.EndsWith("login: ") Then 'If the telnet asks you to Enter the login name the Send_Sub will do the job
                        bgw.ReportProgress(BGWaction.Send, $"VK3OHM-9{vbCrLf}")
                        bgw.ReportProgress(BGWaction.Logged, "")
                    ElseIf command.EndsWith("password: ") Then 'If the telnet asks you to Enter the Password the Send_Sub will do the job
                        bgw.ReportProgress(BGWaction.Send, $"rubbish{vbCrLf}")
                    ElseIf command = ">>" Then
                        bgw.ReportProgress(BGWaction.Busy, "")   ' got prompt. Cluster not busy
                    End If
                    Application.DoEvents()
                    cr = buffer.IndexOf(vbCr)
                End While
            End If
            bgw.ReportProgress(BGWaction.Busy, "")
            If GetSpots Then
                bgw.ReportProgress(BGWaction.Send, $"show/dx A25R{vbCrLf}")
                Thread.Sleep(500)
                bgw.ReportProgress(BGWaction.Send, $"show/dx TX7L{vbCrLf}")
                Thread.Sleep(500)
                bgw.ReportProgress(BGWaction.Send, $"show/dx 7O8AD{vbCrLf}")
            End If
            Thread.Sleep(5000)
        Loop While True

    End Sub
    Private Sub bgw_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles bgw.ProgressChanged
        Select Case e.ProgressPercentage
            Case BGWaction.TextBox : Me.TextBox1.AppendText(CType(e.UserState, String))     ' display string in textbox
            Case BGWaction.Send : Dim st As String
                st = CType(e.UserState, String)
                TextBox1.AppendText($"---> {st}")
                Send_Sub(st)       ' send command to cluster
            Case BGWaction.Logged : LoggedIn = True
            Case BGWaction.Busy : ClusterBusy = False
        End Select

    End Sub
    Private Sub frmCluster_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        Cluster.Close()
    End Sub
End Class