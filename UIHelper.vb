Public Module UIHelper
    Public Sub AppendTextSafe(textBox As TextBox, text As String)
        If textBox.InvokeRequired Then
            textBox.Invoke(New Action(Of TextBox, String)(AddressOf AppendTextSafe), textBox, text)
        Else
            Try
                If text.EndsWith(vbCr) Then text &= vbLf
                textBox.AppendText(text)
                ' Limit textbox to last 50 lines
                If textBox.Lines.Length > 50 Then
                    Dim lines = textBox.Lines
                    textBox.Lines = lines.Skip(lines.Length - 50).ToArray()
                End If
                textBox.SelectionStart = textBox.Text.Length
                textBox.ScrollToCaret()

            Catch ex As Exception
                Debug.WriteLine($"Error in AppendTextSafe: {ex.Message}")
            End Try
        End If
    End Sub
End Module
