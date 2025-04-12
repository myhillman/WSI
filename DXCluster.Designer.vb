<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmCluster
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
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
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Dim DataGridViewCellStyle1 As DataGridViewCellStyle = New DataGridViewCellStyle()
        TextBox1 = New TextBox()
        DataGridView1 = New DataGridView()
        ComboBox1 = New ComboBox()
        Label1 = New Label()
        CType(DataGridView1, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        ' 
        ' TextBox1
        ' 
        TextBox1.Location = New Point(12, 12)
        TextBox1.Multiline = True
        TextBox1.Name = "TextBox1"
        TextBox1.ScrollBars = ScrollBars.Vertical
        TextBox1.Size = New Size(961, 88)
        TextBox1.TabIndex = 0
        ' 
        ' DataGridView1
        ' 
        DataGridView1.AllowUserToAddRows = False
        DataGridView1.AllowUserToDeleteRows = False
        DataGridView1.AllowUserToOrderColumns = True
        DataGridView1.AllowUserToResizeRows = False
        DataGridViewCellStyle1.BackColor = Color.FromArgb(CByte(224), CByte(224), CByte(224))
        DataGridView1.AlternatingRowsDefaultCellStyle = DataGridViewCellStyle1
        DataGridView1.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        DataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        DataGridView1.Location = New Point(12, 106)
        DataGridView1.Name = "DataGridView1"
        DataGridView1.Size = New Size(961, 536)
        DataGridView1.TabIndex = 1
        ' 
        ' ComboBox1
        ' 
        ComboBox1.FormattingEnabled = True
        ComboBox1.Location = New Point(988, 32)
        ComboBox1.Name = "ComboBox1"
        ComboBox1.Size = New Size(121, 23)
        ComboBox1.TabIndex = 2
        ' 
        ' Label1
        ' 
        Label1.AutoSize = True
        Label1.Location = New Point(1020, 12)
        Label1.Name = "Label1"
        Label1.Size = New Size(53, 15)
        Label1.TabIndex = 3
        Label1.Text = "Max Age"
        ' 
        ' frmCluster
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1130, 654)
        Controls.Add(Label1)
        Controls.Add(ComboBox1)
        Controls.Add(DataGridView1)
        Controls.Add(TextBox1)
        Name = "frmCluster"
        Text = "DX Cluster"
        CType(DataGridView1, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents TextBox1 As TextBox
    Friend WithEvents DataGridView1 As DataGridView
    Friend WithEvents ComboBox1 As ComboBox
    Friend WithEvents Label1 As Label
End Class
