Public Class SulekMessage
    Public Name As String
    Public ID As String
    Public IsOutgoing As Boolean
    Public ClassName As String
    Public ClassNamespace As String
    Public IsConfident As Boolean
    Public Sub New(Name As String, ID As String, IsOutgoing As Boolean, ClassName As String, ClassNamespace As String, Optional IsConfident As Boolean = True)
        Me.Name = Name
        Me.ID = ID
        Me.IsOutgoing = IsOutgoing
        Me.ClassName = ClassName
        Me.ClassNamespace = ClassNamespace
        Me.IsConfident = IsConfident
    End Sub
End Class
