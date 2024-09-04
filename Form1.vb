Imports System.ComponentModel
Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class Form1
    Dim Windows As New Dictionary(Of String, Point)     ' location of windows saved from last time
    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TextBox1.KeyPress
        'e.Handled = True
        If e.KeyChar = Microsoft.VisualBasic.ChrW(Keys.Return) And Len(TextBox1.Text) >= 3 Then
            ' Check that a form for that callsign is not already open
            Dim myforms As FormCollection = Application.OpenForms
            For Each frm As Form In myforms
                If frm.Text = TextBox1.Text Then Exit Sub
            Next
            CreateMatrix(TextBox1.Text)
        End If
    End Sub
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If My.Settings.Windows <> "" Then
            Windows = JsonSerializer.Deserialize(Of Dictionary(Of String, Point))(My.Settings.Windows)    ' deserialize window settings
            Dim value As Point
            If Windows.TryGetValue(Me.Text, value) Then
                Me.Location = value      ' reposition main window
            End If
        End If
        TextBox2.Text = My.Settings.OnStartup           ' display list of on startup calls
        Dim calls = Split(Me.TextBox2.Text, ",")       ' split list of call
        For callsign = LBound(calls) To UBound(calls)
            CreateMatrix(calls(callsign))               ' display startup calls
        Next
    End Sub
    Private Sub CreateMatrix(callsign As String)
        ' Open a new form for this callsign
        Dim value As Point
        Dim dlg = New WSI With {
        .Text = callsign,
        .ShowInTaskbar = True
    }
        ' show the form
        dlg.Show()
        If Windows.TryGetValue(callsign, value) Then
            dlg.Location = value
        End If
    End Sub

    Private Sub TextBox2_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TextBox2.KeyPress
        Dim regexp = New Regex("[a-zA-Z0-9\/,]")
        e.Handled = Not (regexp.IsMatch(e.KeyChar) Or e.KeyChar = vbCrLf Or e.KeyChar = vbBack)         ' only allow valid characters
    End Sub
    Private Sub TextBox2_TextChanged(sender As Object, e As EventArgs) Handles TextBox2.TextChanged
        ' Save on startup settings when they change
        My.Settings.OnStartup = TextBox2.Text
        My.Settings.Save()
    End Sub

    Private Sub Form1_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        ' Save position of all open windows
        Windows.Clear()
        Dim myforms As FormCollection = Application.OpenForms
        For Each frm As Form In myforms
            Windows.Add(frm.Text, frm.Location)
        Next
        Dim WindowsSetting = JsonSerializer.Serialize(Windows)
        My.Settings.Windows = WindowsSetting
        My.Settings.Save()
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Dim dlg = New frmCluster
        ' show the form
        dlg.Show()
    End Sub
End Class
