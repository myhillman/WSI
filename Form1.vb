Imports System.ComponentModel
Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class Form1
    ' Dictionary to store the saved locations of windows from the last session
    Dim Windows As New Dictionary(Of String, Point)
    Public SelectedCall As String

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
            SelectedCall = TextBox1.Text
            CreateMatrix(SelectedCall)
        End If
    End Sub

    ' Handles the Load event for Form1
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Load saved window positions from settings
        If My.Settings.Windows <> "" Then
            Windows = JsonSerializer.Deserialize(Of Dictionary(Of String, Point))(My.Settings.Windows) ' Deserialize window settings
            Dim value As Point
            ' Restore the position of the main window if it was saved
            If Windows.TryGetValue(Me.Text, value) Then
                Me.Location = value
            End If
        End If

        ' Load and display the list of startup calls
        TextBox2.Text = My.Settings.OnStartup
        Dim calls = Split(Me.TextBox2.Text, ",") ' Split the list of calls
        For callsign = LBound(calls) To UBound(calls)
            CreateMatrix(calls(callsign)) ' Create forms for each startup call
        Next
    End Sub

    ' Creates a new form for the given callsign
    Private Sub CreateMatrix(callsign As String)
        ' Create a new instance of the WSI form
        Dim value As Point
        Dim dlg = New WSI With {
            .Text = callsign,          ' Set the form's title to the callsign
            .ShowInTaskbar = True      ' Ensure the form is shown in the taskbar
        }
        ' Show the form
        dlg.Show()
        ' Restore the form's position if it was saved
        If Windows.TryGetValue(callsign, value) Then
            dlg.Location = value
        End If
    End Sub

    ' Handles the KeyPress event for TextBox2
    Private Sub TextBox2_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TextBox2.KeyPress
        ' Allow only alphanumeric characters, forward slashes, commas, Enter, and Backspace
        Dim regexp = New Regex("[a-zA-Z0-9\/,]")
        e.Handled = Not (regexp.IsMatch(e.KeyChar) Or e.KeyChar = vbCrLf Or e.KeyChar = vbBack)
    End Sub

    ' Handles the TextChanged event for TextBox2
    Private Sub TextBox2_TextChanged(sender As Object, e As EventArgs) Handles TextBox2.TextChanged
        ' Save the updated startup calls to settings
        My.Settings.OnStartup = TextBox2.Text
        My.Settings.Save()
    End Sub

    ' Handles the Closing event for Form1
    Private Sub Form1_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        ' Save the positions of all open windows
        Windows.Clear()
        Dim myforms As FormCollection = Application.OpenForms
        For Each frm As Form In myforms
            Windows.Add(frm.Text, frm.Location) ' Add each form's title and location to the dictionary
        Next
        ' Serialize the window positions and save them to settings
        Dim WindowsSetting = JsonSerializer.Serialize(Windows)
        My.Settings.Windows = WindowsSetting
        My.Settings.Save()
    End Sub

    ' Handles the Click event for Button1
    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        ' Create and show a new instance of the frmCluster form
        Dim dlg = New frmCluster
        dlg.Show()
    End Sub

End Class
