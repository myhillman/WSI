<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        TextBox1 = New TextBox()
        Label1 = New Label()
        Label2 = New Label()
        TextBox2 = New TextBox()
        Button1 = New Button()
        TabControl1 = New TabControl()
        Button3 = New Button()
        Button4 = New Button()
        PrintDocument1 = New Printing.PrintDocument()
        Button5 = New Button()
        SuspendLayout()
        ' 
        ' TextBox1
        ' 
        TextBox1.AcceptsReturn = True
        TextBox1.CausesValidation = False
        TextBox1.CharacterCasing = CharacterCasing.Upper
        TextBox1.ImeMode = ImeMode.NoControl
        TextBox1.Location = New Point(84, 41)
        TextBox1.Name = "TextBox1"
        TextBox1.Size = New Size(132, 23)
        TextBox1.TabIndex = 0
        TextBox1.TabStop = False
        TextBox1.WordWrap = False
        ' 
        ' Label1
        ' 
        Label1.AutoSize = True
        Label1.Location = New Point(5, 44)
        Label1.Name = "Label1"
        Label1.Size = New Size(49, 15)
        Label1.TabIndex = 1
        Label1.Text = "Callsign"
        ' 
        ' Label2
        ' 
        Label2.AutoSize = True
        Label2.Location = New Point(5, 15)
        Label2.Name = "Label2"
        Label2.Size = New Size(64, 15)
        Label2.TabIndex = 2
        Label2.Text = "On Startup"
        ' 
        ' TextBox2
        ' 
        TextBox2.AcceptsReturn = True
        TextBox2.CharacterCasing = CharacterCasing.Upper
        TextBox2.Location = New Point(84, 12)
        TextBox2.Name = "TextBox2"
        TextBox2.Size = New Size(486, 23)
        TextBox2.TabIndex = 3
        ' 
        ' Button1
        ' 
        Button1.Location = New Point(396, 39)
        Button1.Name = "Button1"
        Button1.Size = New Size(96, 29)
        Button1.TabIndex = 4
        Button1.Text = "DX Cluster"
        Button1.UseVisualStyleBackColor = True
        ' 
        ' TabControl1
        ' 
        TabControl1.Location = New Point(21, 70)
        TabControl1.Name = "TabControl1"
        TabControl1.SelectedIndex = 0
        TabControl1.Size = New Size(549, 297)
        TabControl1.TabIndex = 6
        ' 
        ' Button3
        ' 
        Button3.Location = New Point(81, 373)
        Button3.Name = "Button3"
        Button3.Size = New Size(75, 29)
        Button3.TabIndex = 7
        Button3.Text = "Print"
        Button3.UseVisualStyleBackColor = True
        ' 
        ' Button4
        ' 
        Button4.Location = New Point(167, 373)
        Button4.Name = "Button4"
        Button4.Size = New Size(75, 29)
        Button4.TabIndex = 8
        Button4.Text = "Refresh"
        Button4.UseVisualStyleBackColor = True
        ' 
        ' Button5
        ' 
        Button5.Location = New Point(270, 373)
        Button5.Name = "Button5"
        Button5.Size = New Size(75, 29)
        Button5.TabIndex = 9
        Button5.Text = "Close"
        Button5.UseVisualStyleBackColor = True
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        ClientSize = New Size(586, 413)
        Controls.Add(Button5)
        Controls.Add(Button4)
        Controls.Add(Button3)
        Controls.Add(TabControl1)
        Controls.Add(Button1)
        Controls.Add(TextBox2)
        Controls.Add(Label2)
        Controls.Add(Label1)
        Controls.Add(TextBox1)
        Name = "Form1"
        Text = "Worked Status Indicator"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents TextBox1 As TextBox
    Friend WithEvents Label1 As Label
    Friend WithEvents Label2 As Label
    Friend WithEvents TextBox2 As TextBox
    Friend WithEvents Button1 As Button
    Friend WithEvents TabControl1 As TabControl
    Friend WithEvents Button3 As Button
    Friend WithEvents Button4 As Button
    Friend WithEvents PrintDocument1 As Printing.PrintDocument
    Friend WithEvents Button5 As Button
End Class
