Imports System.IO
Imports System.Text.Json.Nodes
Imports Flazzy.ABC
Imports Flazzy.ABC.AVM2.Instructions

Module Program

    Sub Main(args As String())
        Directory.SetCurrentDirectory(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName))
        Console.Title = "Sulek for HabboAir"
        Try
            Console.WriteLine("Enter client filename:")
            Dim TargetSwf As String = Console.ReadLine()
            Console.WriteLine("")
            If File.Exists(TargetSwf) = False Then
                Console.WriteLine("Client not found")
                Console.WriteLine("")
                Exit Try
            End If
            Console.WriteLine("Dumping messages ...")
            Dim Flash = New FlashFile(TargetSwf)
            Flash.Disassemble()
            Dim SulekAPIRevision As String = "UNKNOWN"
            Dim SulekOutgoingMessages As New List(Of SulekMessage)
            Dim SulekIncomingMessages As New List(Of SulekMessage)
            Dim SulekUnknownMessages As New List(Of SulekMessage)
            Dim TempClass = GetClassByRealClassName(Flash.AbcFiles, "HabboMessages")
            Dim TempClassCode = TempClass.Constructor.Body.ParseCode()
            Dim OutgoingTypeConstName As String = TempClass.Traits(1).QName.Name
            For i As Integer = 0 To TempClassCode.Count - 1
                Dim TempClassCodeInstruction As ASInstruction = TempClassCode(i)
                Dim MessageDetected As Boolean = False
                Dim MessageOutgoing As Boolean = False
                Dim MessageID As Integer = 0
                Dim MessageOldClassName As String = ""
                Dim MessageNewClassName As String = ""
                Dim MessageNamespace As String = ""
                If TempClassCodeInstruction.OP = OPCode.PushByte Then
                    MessageID = CType(TempClassCodeInstruction, PushByteIns).Value
                    MessageDetected = True
                End If
                If TempClassCodeInstruction.OP = OPCode.PushShort Then
                    MessageID = CType(TempClassCodeInstruction, PushShortIns).Value
                    MessageDetected = True
                End If
                If MessageDetected = True Then
                    TempClassCodeInstruction = TempClassCode(i - 1)
                    If TempClassCodeInstruction.OP = OPCode.GetLex Then
                        If CType(TempClassCodeInstruction, GetLexIns).TypeName.Name = OutgoingTypeConstName Then
                            MessageOutgoing = True
                        End If
                    End If
                    TempClassCodeInstruction = TempClassCode(i + 1)
                    If TempClassCodeInstruction.OP = OPCode.GetLex Then
                        MessageOldClassName = CType(TempClassCodeInstruction, GetLexIns).TypeName.Name
                        MessageNamespace = CType(TempClassCodeInstruction, GetLexIns).TypeName.[Namespace].Name
                        MessageNewClassName = MessageOldClassName
                        If MessageID = 4000 And MessageOutgoing = True Then 'Outgoing 4000=ClientHello
                            SulekAPIRevision = GetRevisionByClassName(Flash.AbcFiles, MessageOldClassName)
                        End If
                        If MessageOldClassName.StartsWith("_-") Then 'ClassName is Encrypted
                            Try 'Try to get name using Instance constructor name
                                MessageNewClassName = GetRealInstanceName(GetInstanceByName(Flash.AbcFiles, MessageOldClassName))
                            Catch
                                Try 'Try to get name using Class name reference
                                    MessageNewClassName = GetRealClassName(GetClassByName(Flash.AbcFiles, MessageOldClassName))
                                Catch
                                    Try 'Try to get name using Instance parser method
                                        MessageNewClassName = GetRealInstanceNameWithParserMethod(Flash.AbcFiles, GetInstanceByName(Flash.AbcFiles, MessageOldClassName))
                                    Catch 'Failed to get name, will be added as Unknown message
                                        SulekUnknownMessages.Add(New SulekMessage(MessageOldClassName, MessageID, MessageOutgoing, MessageOldClassName, MessageNamespace))
                                        Continue For
                                    End Try
                                End Try
                            End Try
                        End If
                    End If
                    If MessageOutgoing = True Then
                        SulekOutgoingMessages.Add(New SulekMessage(CleanNewClassName(MessageNewClassName), MessageID, MessageOutgoing, MessageOldClassName, MessageNamespace))
                    Else
                        SulekIncomingMessages.Add(New SulekMessage(CleanNewClassName(MessageNewClassName), MessageID, MessageOutgoing, MessageOldClassName, MessageNamespace))
                    End If
                End If
            Next
            If SulekIncomingMessages.Count + SulekOutgoingMessages.Count + SulekUnknownMessages.Count = 0 Then
                Console.WriteLine("No messages found.")
            Else
                If SulekUnknownMessages.Count > 0 Then
                    Console.WriteLine("Trying to fix unknown messages ...")
                    FixUnknownMessages(Flash.AbcFiles, SulekUnknownMessages, SulekOutgoingMessages, SulekIncomingMessages)
                End If
                Console.WriteLine("")
                Dim SulekJSON As JsonObject = New JsonObject
                SulekJSON.Add("revision", SulekAPIRevision)
                SulekJSON.Add("lastCheckedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffff") & "+00:00")
                Dim SulekMessagesJObject As New JsonObject
                SulekMessagesJObject.Add("outgoing", GetSulekMessagesAsJArray(SulekOutgoingMessages))
                SulekMessagesJObject.Add("incoming", GetSulekMessagesAsJArray(SulekIncomingMessages))
                SulekJSON.Add("messages", SulekMessagesJObject)
                File.WriteAllText("SULEK_API-" & SulekAPIRevision, SulekJSON.ToString.Replace("\u002B", "+"))
                Console.WriteLine("Destination file: " & "Sulek_API-" & SulekAPIRevision)
                Console.WriteLine("Saved " & SulekOutgoingMessages.Count & " outgoing messages and " & SulekIncomingMessages.Count & " incoming messages.")
                Console.WriteLine("Unknown messages names: " & SulekUnknownMessages.Count)
                Console.WriteLine("Known messages names rate: " & Math.Round((SulekOutgoingMessages.Count + SulekIncomingMessages.Count - SulekUnknownMessages.Count) / (SulekOutgoingMessages.Count + SulekIncomingMessages.Count) * 100, 2) & "%")
            End If
            Console.WriteLine("")
        Catch ex As Exception
            Console.WriteLine("")
            Console.WriteLine("An error occurred:")
            Console.WriteLine("[" & ex.Message & "]")
            Console.WriteLine("")
        End Try
        Console.WriteLine("Press ENTER to exit ...")
        Do While Console.ReadKey(True).Key = ConsoleKey.Enter = False
            'Wait until user press ENTER
        Loop
        Environment.Exit(0)
    End Sub

    Sub FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages As List(Of SulekMessage), ClassNamesReferences As List(Of String), NewMessageName As String, Optional ReverseReferencesListOrder As Boolean = False)
        If ReverseReferencesListOrder = True Then
            ClassNamesReferences.Reverse()
        End If
        For Each ClassNameReference In ClassNamesReferences
            For Each SulekUnknownMessage In SulekUnknownMessages
                If SulekUnknownMessage.Name = ClassNameReference Then
                    'Console.WriteLine(SulekUnknownMessage.Name & "=" & NewMessageName)
                    SulekUnknownMessage.Name = NewMessageName
                    Return
                End If
            Next
        Next
    End Sub

    Sub FixUnknownMessages(ABCFiles As List(Of ABCFile), SulekUnknownMessages As List(Of SulekMessage), SulekOutgoingMessages As List(Of SulekMessage), SulekIncomingMessages As List(Of SulekMessage))
        Try
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByRealName(ABCFiles, "NewIncomingMessages"), "onUserObject"), "GetUserEventCatsMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "QuestCompleted"), "onNextQuest"), "OpenQuestTrackerMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "sendChatTypingMessage"), "StartTypingMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "sendChatTypingMessage"), "CancelTypingMessageComposer", True)
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "CameraWidgetHandler"), "sendInitCameraMessage"), "RequestCameraConfigurationMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "MarketplaceModel"), "startOfferMaking"), "GetMarketplaceCanMakeOfferMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboQuestEngine"), "requestSeasonalQuests"), "GetSeasonalQuestsOnlyMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboCatalog"), "getGiftWrappingConfiguration"), "GetGiftWrappingConfigurationComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "sendRoomDimmerGetPresetsMessage"), "RoomDimmerGetPresetsMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "sendRoomDimmerGetPresetsMessage"), "RoomDimmerGetPresetsMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "BCFloorPlanEditor"), "visible"), "GetRoomEntryTileMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "BCFloorPlanEditor"), "visible"), "GetOccupiedTilesMessageComposer", True)
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "CatalogPromo"), "onCatalogPublished"), "GetSeasonalCalendarDailyComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboFriendList"), "getFriendRequests"), "GetFriendRequestsMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "PromoArticleWidget"), "refresh"), "GetPromoArticlesMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboFriendList"), "initComponent"), "MessengerInitMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "sendRoomDimmerChangeStateMessage"), "RoomDimmerChangeStateMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByRealName(ABCFiles, "RoomMessageHandler"), "onFurnitureAliases"), "GetHeightMapMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "BonusRarePromoWidget"), "requestBonusRareInfo"), "GetBonusRareInfoMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "ClubGiftController"), "widget"), "GetClubGiftMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboCatalog"), "sendGetBundleDiscountRuleset"), "GetBundleDiscountRulesetComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomSession"), "quit"), "QuitMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "CommunityGoalWidget"), "requestCommunityGoalProgress"), "GetCommunityGoalProgressMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "NextLimitedRareCountdownWidget"), "requestLimitedOfferAppearingNextMessage"), "GetLimitedOfferAppearingNextComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "MarketplaceModel"), "buyMarketplaceTokens"), "BuyMarketplaceTokensMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "MarketPlaceLogic"), "getConfiguration"), "GetMarketplaceConfigurationMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "RoomCompetitionController"), "sendRoomCompetitionInit"), "RoomCompetitionInitMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "MyRoomsTabPageDecorator"), "onCreateRoomClick"), "CanCreateRoomMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboFriendList"), "sendFriendListUpdate"), "FriendListUpdateMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboQuestEngine"), "requestQuests"), "GetQuestsMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "HabboHelp"), "queryForGuideReportingStatus"), "GuideAdvertisementReadMessageComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "ExpiringCatalogPageWidget"), "refresh"), "GetCatalogPageWithEarliestExpiryComposer")
            FixUnknownMessagesFromClassNamesReferences(SulekUnknownMessages, GetClassNamesReferencesFromInstanceFunction(GetInstanceByName(ABCFiles, "QuestsList"), "onCancelQuest"), "RejectQuestMessageComposer")
        Catch
            Console.WriteLine("Messages names cannot be fixed")
        End Try
        Dim CurrentUnknownMessageNumber = 1
        Dim IncludeUnknownMessages = True
        If SulekUnknownMessages.FindAll(Function(x) x.Name.StartsWith("_-")).Count > 0 Then
            Console.WriteLine("")
            Console.WriteLine("Do you want to include messages with unknown names? (y/n)")
            If Console.ReadKey(True).Key = ConsoleKey.N Then
                IncludeUnknownMessages = False
            End If
        End If
        For Each SulekUnknownMessage In SulekUnknownMessages
            SulekUnknownMessage.Name = CleanNewClassName(SulekUnknownMessage.Name)
            If SulekUnknownMessage.Name.StartsWith("_-") Then
                SulekUnknownMessage.Name = "UnknownMessage_" & CurrentUnknownMessageNumber
                SulekUnknownMessage.IsConfident = False
                CurrentUnknownMessageNumber += 1
            End If
            If IncludeUnknownMessages = True Then
                If SulekUnknownMessage.IsOutgoing Then
                    SulekOutgoingMessages.Add(SulekUnknownMessage)
                Else
                    SulekIncomingMessages.Add(SulekUnknownMessage)
                End If
            End If
        Next
        For Each SulekUnknownMessage In SulekUnknownMessages.ToList
            If SulekUnknownMessage.Name.StartsWith("UnknownMessage_") = False Then
                SulekUnknownMessages.Remove(SulekUnknownMessage)
            End If
        Next
    End Sub

    Function GetClassByName(ABCFiles As List(Of ABCFile), RequestedClass As String) As ASClass
        For Each ABCfile In ABCFiles
            Try
                Return ABCfile.Classes.First(Function(x) x.QName.Name = RequestedClass)
            Catch
                'Class not found on current ABCFile
            End Try
        Next
        Throw New Exception("Class not found")
    End Function

    Function GetInstanceByName(ABCFiles As List(Of ABCFile), RequestedInstance As String) As ASInstance
        For Each ABCfile In ABCFiles
            Try
                Return ABCfile.Instances.First(Function(x) x.QName.Name = RequestedInstance)
            Catch
                'Instance not found on current ABCFile
            End Try
        Next
        Throw New Exception("Instance not found")
    End Function

    Function GetRealInstanceName(RequestedInstance As ASInstance) As String
        Try
            Dim RealInstanceName = RequestedInstance.Constructor.Name
            If String.IsNullOrWhiteSpace(RealInstanceName) Then
                Throw New Exception("Real Instance name cannot be empty")
            End If
            If RealInstanceName.StartsWith("_-") Then
                Throw New Exception("Invalid real Instance name")
            End If
            Return RealInstanceName
        Catch
            Throw New Exception("Failed to get " & RequestedInstance.QName.Name & " instance name")
        End Try
    End Function

    Function GetRealClassName(RequestedClass As ASClass) As String
        Try
            Dim ClassTrait = RequestedClass.Traits.Concat(RequestedClass.Instance.Traits).FirstOrDefault(Function(x) x.QName.Namespace.Kind = NamespaceKind.Private)
            Dim RealClassName = ClassTrait.QName.Namespace.Name
            Dim RealClassNameDelimiters = {":", "."}
            For Each RealClassNameDelimiter In RealClassNameDelimiters
                If RealClassName.Contains(RealClassNameDelimiter) Then
                    RealClassName = RealClassName.Remove(0, RealClassName.LastIndexOf(RealClassNameDelimiter) + RealClassNameDelimiter.Length)
                End If
            Next
            If String.IsNullOrWhiteSpace(RealClassName) Then
                Throw New Exception("Real Class name cannot be empty")
            End If
            If RealClassName.StartsWith("_-") Then
                Throw New Exception("Invalid real Class name")
            End If
            Return RealClassName
        Catch
            Throw New Exception("Failed to get " & RequestedClass.QName.Name & " class name")
        End Try
    End Function

    Function GetRealInstanceNameWithParserMethod(ABCFiles As List(Of ABCFile), RequestedInstance As ASInstance) As String
        Try
            Dim TempClassCode = RequestedInstance.Constructor.Body.ParseCode()
            For i As Integer = 0 To TempClassCode.Count - 1
                Dim TempClassCodeInstruction As ASInstruction = TempClassCode(i)
                If TempClassCodeInstruction.OP = OPCode.GetLex And TempClassCode(i + 1).OP = OPCode.ConstructSuper Then
                    Dim TempClassParser As ASInstance = GetInstanceByName(ABCFiles, CType(TempClassCodeInstruction, GetLexIns).TypeName.Name)
                    Dim TempClassParserNamespace = TempClassParser.Traits(0).QName.Namespace.Name
                    If TempClassParserNamespace.Contains(":") And TempClassParserNamespace.EndsWith("Parser") Then
                        Dim TempClassParserName = TempClassParserNamespace.Remove(0, TempClassParserNamespace.LastIndexOf(":") + 1)
                        TempClassParserName = TempClassParserName.Remove(TempClassParserName.LastIndexOf("Parser")) & "Event"
                        If String.IsNullOrWhiteSpace(TempClassParserName) Then
                            Throw New Exception("Real Instance name cannot be empty")
                        End If
                        If TempClassParserName.StartsWith("_-") Then
                            Throw New Exception("Invalid real Instance name")
                        End If
                        Return TempClassParserName
                        Exit For
                    End If
                End If
            Next
            Throw New Exception("Real Instance name not found")
        Catch
            Throw New Exception("Failed to get " & RequestedInstance.QName.Name & " instance name with parser method")
        End Try
    End Function

    Function GetClassByRealClassName(ABCFiles As List(Of ABCFile), RealClassName As String) As ASClass
        Try
            For Each ABCfile In ABCFiles
                For Each TempClass In ABCfile.Classes
                    Try
                        Dim TempClassName As String = GetRealClassName(TempClass)
                        If TempClassName = RealClassName Then
                            Return TempClass
                        End If
                    Catch
                        'Failed to get real Class name, skip to next
                    End Try
                Next
            Next
            Throw New Exception("Requested Class not found")
        Catch
            Throw New Exception("Failed to get " & RealClassName & " Class by real name")
        End Try
    End Function

    Function GetInstanceByRealName(ABCFiles As List(Of ABCFile), RealInstanceName As String) As ASInstance
        Try
            For Each ABCfile In ABCFiles
                For Each TempInstance In ABCfile.Instances
                    Try
                        Dim TempInstanceName As String = GetRealInstanceName(TempInstance)
                        If TempInstanceName = RealInstanceName Then
                            Return TempInstance
                        End If
                    Catch
                        'Failed to get real Instance name, skip to next
                    End Try
                Next
            Next
            Throw New Exception("Requested Instance not found")
        Catch
            Throw New Exception("Failed to get " & RealInstanceName & " Instance by real name")
        End Try
    End Function

    Function GetClassNamesReferencesFromInstanceFunction(OriginInstance As ASInstance, Optional FunctionName As String = Nothing, Optional FunctionReturnTypeName As String = Nothing, Optional FunctionParamCount As Integer = Nothing) As List(Of String)
        Dim ClassNamesReferences As New List(Of String)
        Dim OriginInstanceMethods
        If FunctionParamCount = Nothing Then
            OriginInstanceMethods = OriginInstance.GetMethods(FunctionName, FunctionReturnTypeName)
        Else
            OriginInstanceMethods = OriginInstance.GetMethods(FunctionName, FunctionReturnTypeName, FunctionParamCount)
        End If
        For Each OriginInstanceMethod In OriginInstanceMethods
            Dim OriginInstanceMethodCode = OriginInstanceMethod.Body.ParseCode()
            Dim ReferencedConstructPropCount As Integer = 0
            For i As Integer = 0 To OriginInstanceMethodCode.Count - 1
                Dim TempClassCodeInstruction As ASInstruction = OriginInstanceMethodCode(i)
                If TempClassCodeInstruction.OP = OPCode.ConstructProp Then
                    ReferencedConstructPropCount += 1
                    ClassNamesReferences.Add(CType(TempClassCodeInstruction, ConstructPropIns).PropertyName.Name)
                End If
            Next
        Next
        If ClassNamesReferences.Count > 0 Then
            Return ClassNamesReferences
        Else
            Throw New Exception("Failed to get Classes Names references from Instance Function")
        End If
    End Function

    Function GetRevisionByClassName(ABCFiles As List(Of ABCFile), OldClassName As String) As String
        Try
            Dim TempClassCode = GetInstanceByName(ABCFiles, OldClassName).GetMethod(Nothing, "Array").Body.ParseCode()
            For i As Integer = 0 To TempClassCode.Count - 1
                Dim TempClassCodeInstruction As ASInstruction = TempClassCode(i)
                If TempClassCodeInstruction.OP = OPCode.PushString Then
                    Return CType(TempClassCodeInstruction, PushStringIns).Value
                End If
            Next
            Throw New Exception("Revision string read error")
        Catch
            Throw New Exception("Client Revision not found")
        End Try
    End Function

    Function GetSulekMessagesAsJArray(SulekMessages As List(Of SulekMessage)) As JsonArray
        Dim SulekMessagesJArray As New JsonArray()
        For Each SulekMessage In SulekMessages
            Dim NewSulekJMessage As New JsonObject
            NewSulekJMessage.Add("id", SulekMessage.ID)
            NewSulekJMessage.Add("name", SulekMessage.Name)
            NewSulekJMessage.Add("asNamespace", SulekMessage.ClassNamespace)
            NewSulekJMessage.Add("asClass", SulekMessage.ClassName)
            NewSulekJMessage.Add("IsConfident", SulekMessage.IsConfident)
            SulekMessagesJArray.Add(NewSulekJMessage)
        Next
        Return SulekMessagesJArray
    End Function

    Function CleanNewClassName(NewClassName As String) As String
        'Dim CleanedClassName = NewClassName & "]"
        'CleanedClassName = CleanedClassName.Replace("MessageEvent]", "")
        'CleanedClassName = CleanedClassName.Replace("MessageComposer]", "")
        'CleanedClassName = CleanedClassName.Replace("MessageParser]", "")
        'CleanedClassName = CleanedClassName.Replace("Composer]", "")
        'CleanedClassName = CleanedClassName.Replace("Event]", "")
        'CleanedClassName = CleanedClassName.Replace("]", "")
        'Return CleanedClassName
        Return NewClassName
    End Function

End Module
