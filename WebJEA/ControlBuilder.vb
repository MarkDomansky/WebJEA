Public Class ControlBuilder
    Private dlog As NLog.Logger = NLog.LogManager.GetCurrentClassLogger()

    Public Sub AddControls(controls As List(Of HtmlControl), form As HtmlForm, BeforeControl As HtmlGenericControl)

        For Each ctrl As HtmlControl In controls
            BeforeControl.Controls.Add(ctrl)
        Next
        form.Controls.AddAt(form.Controls.IndexOf(BeforeControl), New LiteralControl("<br />"))

    End Sub

    Public Function NewControl(pg As Page, params As List(Of PSCmdParam)) As List(Of HtmlControl)
        Dim retcontrols As New List(Of HtmlControl)

        If Not IsNothing(params) Then
            For Each param As PSCmdParam In params
                If (param.Name.ToUpper().StartsWith("WEBJEA")) Then
                    'do nothing to display.  This will be handled internally.
                ElseIf param.IsSelect Then
                    retcontrols.Add(NewControlStringSelect(pg, param))
                Else
                    Select Case param.ParamType
                        Case PSCmdParam.ParameterType.PSString
                            retcontrols.Add(NewControlString(pg, param))
                        Case PSCmdParam.ParameterType.PSBoolean
                            retcontrols.Add(NewControlSwitch(pg, param))
                        Case PSCmdParam.ParameterType.PSDate
                            retcontrols.Add(NewControlDate(pg, param))
                        Case PSCmdParam.ParameterType.PSFloat
                            retcontrols.Add(NewControlString(pg, param))
                        Case PSCmdParam.ParameterType.PSInt
                            retcontrols.Add(NewControlString(pg, param))
                        Case Else
                            dlog.Warn("ControlBuilder: Defaulting to string for unknown type: " & param.VarType & " paramname: " & param.Name)
                            retcontrols.Add(NewControlString(pg, param))
                    End Select
                End If

            Next
        End If

        Return retcontrols
    End Function

    Public Function NewVerboseControl(pg As Page) As HtmlControl
        Dim objRow As HtmlGenericControl = NewControl("div", "checkbox verbose-control")

        Dim objLabel As New Label

        Dim strLabel As String = "Verbose"
        Dim objName As HtmlControl = NewControl("span", "form-label", strLabel)

        Dim objControl As New CheckBox
        objControl.ID = "chkWebJEAVerbose"
        objLabel.AssociatedControlID = objControl.ID
        objControl.Checked = False

        objLabel.Controls.Add(objControl)
        objLabel.Controls.Add(objName)

        objRow.Controls.Add(objLabel)

        Return objRow
    End Function

    Private Sub AddMessageHelp(messageString As String, parentObj As Control)
        Dim helpmsgtag As HtmlGenericControl = NewControl("span", "help-message", messageString)
        parentObj.Controls.Add(helpmsgtag)
    End Sub

    Private Sub AddMessageHelpDetail(messageString As String, parentObj As Control)
        Dim helpmsgtag As HtmlGenericControl = NewControl("p", "help-block", messageString)
        parentObj.Controls.Add(helpmsgtag)
    End Sub

    Private Sub AddMessageRequired(parentObj As Control)
        Dim objReqOpt As HtmlControl = NewControl("span", "reqopt", "Required")
        parentObj.Controls.Add(objReqOpt)
    End Sub

    Private Function NewControlString(pg As Page, param As PSCmdParam) As HtmlControl

        Dim objRow As HtmlGenericControl = NewControl("div", "form-group")

        Dim objLabel As Label = NewControlLabel(param.Name)
        If Not String.IsNullOrWhiteSpace(param.HelpMessage) Then
            objLabel.Text = param.HelpMessage
        End If

        Dim objControl As New TextBox
        objControl.ID = param.FieldName
        objControl.CssClass += " form-control"
        If param.IsMultiValued Or param.DirectiveMultiline Then
            objControl.TextMode = TextBoxMode.MultiLine
            objControl.Rows = 5
            objControl.Columns = 100
        End If
        objLabel.AssociatedControlID = objControl.ID

        If param.IsMultiValued Then
            If Not (param.DefaultValue Is Nothing) Then
                Dim defval As List(Of String) = param.DefaultValue
                objControl.Text = String.Join(vbCrLf, defval)
            End If
        Else
            objControl.Text = param.DefaultValue
        End If
        objControl.Text = ReadGetPost(pg, param.Name, objControl.Text)

        objRow.Controls.Add(objLabel)
        If param.IsMandatory Then AddMessageRequired(objRow)
        objRow.Controls.Add(objControl)
        If Not String.IsNullOrEmpty(param.HelpDetail) Then AddMessageHelpDetail(param.HelpDetail, objRow)

        Dim valctrls As List(Of WebControl) = GetControlValidations(param)
        For Each valctrl In valctrls
            objRow.Controls.Add(valctrl)
        Next

        Return objRow

    End Function

    Private Function NewControlDate(pg As Page, param As PSCmdParam) As HtmlControl

        Dim objRow As HtmlGenericControl = NewControl("div", "form-group")

        Dim objLabel As Label = NewControlLabel(param.Name)

        Dim objControl As New TextBox
        objControl.ID = param.FieldName
        objControl.CssClass += " form-control"

        If param.DirectiveDateTime Then
            objControl.Attributes.Add("data-type", "datetime")
        Else
            objControl.Attributes.Add("data-type", "date")
        End If
        objLabel.AssociatedControlID = objControl.ID

        If Not String.IsNullOrEmpty(param.DefaultValue) Then
            objControl.Text = param.DefaultValue
        End If
        objControl.Text = ReadGetPost(pg, param.Name, objControl.Text)

        objRow.Controls.Add(objLabel)
        If Not String.IsNullOrWhiteSpace(param.HelpMessage) Then AddMessageHelp(param.HelpMessage, objRow)
        If param.IsMandatory Then AddMessageRequired(objRow)
        objRow.Controls.Add(objControl)
        If Not String.IsNullOrEmpty(param.HelpDetail) Then AddMessageHelpDetail(param.HelpDetail, objRow)

        Dim valctrls As List(Of WebControl) = GetControlValidations(param)
        For Each valctrl In valctrls
            objRow.Controls.Add(valctrl)
        Next

        Return objRow

    End Function

    Private Function NewControlStringSelect(pg As Page, param As PSCmdParam) As HtmlControl
        If param.IsMultiValued Then
            Return NewControlStringListbox(pg, param)
        Else
            Return NewControlStringDropdown(pg, param)
        End If
    End Function

    Private Function NewControlStringListbox(pg As Page, param As PSCmdParam) As HtmlControl

        Dim objRow As HtmlGenericControl = NewControl("div", "form-group")

        Dim objLabel As Label = NewControlLabel(param.Name)

        Dim objControl As New ListBox
        objControl.ID = param.FieldName
        objControl.CssClass += " form-control"
        objControl.SelectionMode = ListSelectionMode.Single
        If param.IsMultiValued Then
            objControl.SelectionMode = ListSelectionMode.Multiple
            If param.AllowedValues.Count < 5 Then
                objControl.Rows = param.AllowedValues.Count
            Else
                objControl.Rows = 5
            End If
        End If
        objLabel.AssociatedControlID = objControl.ID

        Dim defval As New List(Of String)
        Dim postget As String = ReadGetPost(pg, param.Name, "")
        If Not (param.DefaultValue Is Nothing) Then defval = param.DefaultValue
        If postget <> "" Then
            defval = postget.Split(New String() {vbCrLf}, StringSplitOptions.RemoveEmptyEntries).ToList()
        End If

        If param.IsMandatory = False Then
            Dim objLI As New ListItem
            If param.DefaultValue Is Nothing Then
                objLI.Selected = True
            End If
            objLI.Text = "--Select--"
            objLI.Value = "--Select--"
            objControl.Items.Add(objLI)
        End If

        For Each allowedval As String In param.AllowedValues
            Dim objLI As New ListItem
            objLI.Value = allowedval
            objLI.Text = allowedval
            If defval.Contains(allowedval) Then objLI.Selected = True
            objControl.Items.Add(objLI)
        Next

        objRow.Controls.Add(objLabel)
        If Not String.IsNullOrWhiteSpace(param.HelpMessage) Then AddMessageHelp(param.HelpMessage, objRow)
        If param.IsMandatory Then AddMessageRequired(objRow)
        objRow.Controls.Add(objControl)
        If Not String.IsNullOrEmpty(param.HelpDetail) Then AddMessageHelpDetail(param.HelpDetail, objRow)

        Dim valctrls As List(Of WebControl) = GetControlValidations(param)
        For Each valctrl In valctrls
            objRow.Controls.Add(valctrl)
        Next

        Return objRow

    End Function

    Private Function NewControlStringDropdown(pg As Page, param As PSCmdParam) As HtmlControl

        Dim objRow As HtmlGenericControl = NewControl("div", "form-group")

        Dim objLabel As Label = NewControlLabel(param.Name)

        Dim objControl As New DropDownList
        objControl.ID = param.FieldName
        objControl.CssClass += " form-control form-select"
        objLabel.AssociatedControlID = objControl.ID

        Dim defval As String = Nothing
        If Not String.IsNullOrEmpty(param.DefaultValue) Then
            defval = param.DefaultValue
        End If
        defval = ReadGetPost(pg, param.Name, defval)

        If True Then
            Dim objLI As New ListItem
            If (defval Is Nothing) Then
                objLI.Selected = True
            End If
            objLI.Text = "--Select--"
            objLI.Value = ""
            objControl.Items.Add(objLI)
        End If
        For Each allowedval As String In param.AllowedValues
            Dim objLI As New ListItem
            objLI.Value = allowedval
            objLI.Text = allowedval
            If allowedval = defval Then
                objLI.Selected = True
            End If
            objControl.Items.Add(objLI)
        Next

        objRow.Controls.Add(objLabel)
        If Not String.IsNullOrWhiteSpace(param.HelpMessage) Then AddMessageHelp(param.HelpMessage, objRow)
        If param.IsMandatory Then AddMessageRequired(objRow)
        objRow.Controls.Add(objControl)
        If Not String.IsNullOrEmpty(param.HelpDetail) Then AddMessageHelpDetail(param.HelpDetail, objRow)

        Dim valctrls As List(Of WebControl) = GetControlValidations(param)
        For Each valctrl In valctrls
            objRow.Controls.Add(valctrl)
        Next

        Return objRow

    End Function

    Public Function NewControlSwitch(pg As Page, param As PSCmdParam) As HtmlControl

        Dim objRow As HtmlGenericControl = NewControl("div", "checkbox")

        Dim objLabel As New Label

        Dim strLabel As String = param.Name
        Dim objName As HtmlControl = NewControl("span", "form-label", strLabel)

        Dim objControl As New CheckBox
        objControl.ID = param.FieldName
        objLabel.AssociatedControlID = objControl.ID
        Dim testresult As Boolean
        If Boolean.TryParse(param.DefaultValue, testresult) Then
            objControl.Checked = testresult
        End If
        objControl.Checked = ReadGetPost(pg, param.Name, objControl.Checked)

        objLabel.Controls.Add(objControl)
        objLabel.Controls.Add(objName)
        If Not String.IsNullOrWhiteSpace(param.HelpMessage) Then AddMessageHelp(param.HelpMessage, objLabel)
        If param.IsMandatory Then AddMessageRequired(objLabel)

        objRow.Controls.Add(objLabel)
        If Not String.IsNullOrEmpty(param.HelpDetail) Then AddMessageHelpDetail(param.HelpDetail, objRow)

        Dim valctrls As List(Of WebControl) = GetControlValidations(param)
        For Each valctrl In valctrls
            objRow.Controls.Add(valctrl)
        Next

        Return objRow
    End Function

    Private Function NewControlLabel(Text As String) As Label
        Dim objLabel As New Label
        objLabel.Text = Text
        objLabel.CssClass = "form-label"

        Return objLabel
    End Function

    Public Function NewControl(tag As String, cssClass As String, Optional innerText As String = "") As HtmlGenericControl
        Dim row As New HtmlGenericControl(tag)
        row.Attributes.Add("class", cssClass)
        If Not String.IsNullOrEmpty(innerText) Then
            row.InnerText = innerText
        End If

        Return row
    End Function

    Public Function GetControlValidations(param As PSCmdParam) As List(Of WebControl)
        Dim retctrls As New List(Of WebControl)
        For Each valobj As PSCmdParamVal In param.ValidationObjects
            If valobj.Type = PSCmdParamVal.ValType.Mandatory And param.ParamType <> PSCmdParam.ParameterType.PSBoolean Then
                Dim valctrl As New RequiredFieldValidator()
                valctrl.ErrorMessage = "Required Field"
                valctrl.CssClass = "valmsg"
                valctrl.SetFocusOnError = True
                valctrl.ControlToValidate = param.FieldName
                retctrls.Add(valctrl)
            ElseIf valobj.Type = PSCmdParamVal.ValType.Mandatory And param.ParamType = PSCmdParam.ParameterType.PSBoolean Then
                Dim valctrl As New CustomValidator
                valctrl.ClientValidationFunction = "validateMandatoryCheckbox"
                valctrl.ErrorMessage = "You must check the box for " & param.Name & "."
                valctrl.CssClass = "valmsg"
                valctrl.SetFocusOnError = True
                valctrl.Attributes("data-control") = param.FieldName
                retctrls.Add(valctrl)

            ElseIf valobj.Type = PSCmdParamVal.ValType.Length Then
                Dim valctrl As New RegularExpressionValidator()
                valctrl.ValidationExpression = "[\S\s]{" & valobj.LowerLimit & "," & valobj.UpperLimit & "}"
                valctrl.ErrorMessage = "Not in allowed length (" & valobj.LowerLimit & "-" & valobj.UpperLimit & ")"
                valctrl.CssClass = "valmsg"
                valctrl.SetFocusOnError = True
                valctrl.ControlToValidate = param.FieldName
                retctrls.Add(valctrl)
            ElseIf valobj.Type = PSCmdParamVal.ValType.Pattern Then
                Dim valctrl As New RegularExpressionValidator()
                valctrl.ValidationExpression = valobj.Pattern
                valctrl.ErrorMessage = "Did not match pattern: " & valobj.Pattern
                valctrl.CssClass = "valmsg"
                valctrl.SetFocusOnError = True
                valctrl.ControlToValidate = param.FieldName
                retctrls.Add(valctrl)
            ElseIf valobj.Type = PSCmdParamVal.ValType.Range Then
                If param.IsMultiValued Then
                    Dim valctrl As New CustomValidator
                    valctrl.ClientValidationFunction = "validateRangeMultiline"
                    valctrl.ErrorMessage = "Each value must be in allowed range (" & valobj.LowerLimit & "-" & valobj.UpperLimit & ")"
                    valctrl.CssClass = "valmsg"
                    valctrl.Attributes("data-min") = valobj.LowerLimit
                    valctrl.Attributes("data-max") = valobj.UpperLimit
                    If param.ParamType = PSCmdParam.ParameterType.PSFloat Then
                        valctrl.Attributes("data-type") = "float"
                    Else
                        valctrl.Attributes("data-type") = "integer"
                    End If
                    valctrl.SetFocusOnError = True
                    valctrl.ControlToValidate = param.FieldName
                    retctrls.Add(valctrl)
                Else
                    Dim valctrl As New RangeValidator()
                    valctrl.MinimumValue = valobj.LowerLimit
                    valctrl.MaximumValue = valobj.UpperLimit
                    valctrl.ErrorMessage = "Not in allowed range (" & valobj.LowerLimit & "-" & valobj.UpperLimit & ")"
                    valctrl.CssClass = "valmsg"
                    If param.ParamType = PSCmdParam.ParameterType.PSInt Then
                        valctrl.Type = ValidationDataType.Integer
                    ElseIf param.ParamType = PSCmdParam.ParameterType.PSFloat Then
                        valctrl.Type = ValidationDataType.Double
                    ElseIf param.ParamType = PSCmdParam.ParameterType.PSDate Then
                        valctrl.Type = ValidationDataType.Date
                    End If
                    valctrl.SetFocusOnError = True
                    valctrl.ControlToValidate = param.FieldName
                    retctrls.Add(valctrl)
                End If
            ElseIf valobj.Type = PSCmdParamVal.ValType.Count Then
                Dim valctrl As New CustomValidator
                valctrl.ClientValidationFunction = "validateCollection"
                valctrl.ErrorMessage = "Number of selected items not in allowed range (" & valobj.LowerLimit & "-" & valobj.UpperLimit & ")"
                valctrl.CssClass = "valmsg"
                valctrl.Attributes("data-min") = valobj.LowerLimit
                valctrl.Attributes("data-max") = valobj.UpperLimit
                valctrl.SetFocusOnError = True
                valctrl.ControlToValidate = param.FieldName
                retctrls.Add(valctrl)
            ElseIf valobj.Type = PSCmdParamVal.ValType.SetCol Then
                'do nothing, this is handled by forcing a SELECT field
            Else
                dlog.Error("Unknown Validation Rule: " & valobj.Rule)

            End If
        Next

        Return retctrls
    End Function

    Private Function ReadGetPost(pg As Page, param As String, DefaultValue As String) As String
        If pg.Request.Form(param) IsNot Nothing Then
            Return pg.Request.Form(param)
        ElseIf pg.Request.QueryString(param) IsNot Nothing Then
            Return pg.Request.QueryString(param)
        Else
            Return DefaultValue
        End If
    End Function

    Private Function ReadGetPost(pg As Page, param As String, DefaultValue As Boolean) As Boolean
        If pg.Request.Form(param) IsNot Nothing Then
            Return pg.Request.Form(param)
        ElseIf pg.Request.QueryString(param) IsNot Nothing Then
            Return pg.Request.QueryString(param)
        Else
            Return DefaultValue
        End If
    End Function

End Class
