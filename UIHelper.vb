Public Module UIHelper
    Public Sub AppendTextSafe(textBox As TextBox, text As String)
        If textBox.InvokeRequired Then
            textBox.Invoke(New Action(Of String)(Sub(t) AppendTextSafe(textBox, t)), text)
        Else
            textBox.AppendText(text)
            ' Limit lines to 50
            Dim lines = textBox.Lines
            If lines.Length > 50 Then
                textBox.Lines = lines.Skip(lines.Length - 50).ToArray()
            End If
            textBox.SelectionStart = textBox.Text.Length
            textBox.ScrollToCaret()
            Application.DoEvents()
        End If
    End Sub
End Module
