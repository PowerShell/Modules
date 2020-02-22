using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;

namespace Microsoft.PowerShell.PrettyPrinter
{
    public class PrettyPrinter : AstVisitor2
    {
        public static string PrettyPrint(Ast ast)
        {
            var pp = new PrettyPrinter();
            ast.Visit(pp);
            return pp.GetPrettyPrintResult();
        }

        private readonly StringBuilder _sb;

        private readonly string _newline;

        private readonly string _indentStr;

        private readonly string _comma;

        private int _indent;

        public PrettyPrinter()
        {
            _sb = new StringBuilder();
            _newline = "\n";
            _indentStr = "    ";
            _comma = ", ";
            _indent = 0;
        }

        public void Clear()
        {
            _sb.Clear();
        }

        public string GetPrettyPrintResult()
        {
            return _sb.ToString();
        }

        public override AstVisitAction VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            _sb.Append("@(");

            WriteStatementBlock(arrayExpressionAst.SubExpression.Statements, arrayExpressionAst.SubExpression.Traps);

            _sb.Append(")");

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            Intersperse(arrayLiteralAst.Elements, _comma);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            assignmentStatementAst.Left.Visit(this);

            _sb.Append(' ').Append(GetTokenString(assignmentStatementAst.Operator)).Append(' ');

            assignmentStatementAst.Right.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitAttribute(AttributeAst attributeAst)
        {
            _sb.Append('[').Append(attributeAst.TypeName).Append('(');

            bool hadPositionalArgs = false;
            if (attributeAst.PositionalArguments != null && attributeAst.PositionalArguments.Count > 0)
            {
                hadPositionalArgs = true;
                Intersperse(attributeAst.PositionalArguments, _comma);
            }

            if (attributeAst.NamedArguments != null && attributeAst.NamedArguments.Count > 0)
            {
                if (hadPositionalArgs)
                {
                    _sb.Append(_comma);
                }

                Intersperse(attributeAst.NamedArguments, _comma);
            }

            _sb.Append(")]");
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            attributedExpressionAst.Attribute.Visit(this);
            attributedExpressionAst.Child.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitBaseCtorInvokeMemberExpression(BaseCtorInvokeMemberExpressionAst baseCtorInvokeMemberExpressionAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            binaryExpressionAst.Left.Visit(this);

            _sb.Append(' ').Append(GetTokenString(binaryExpressionAst.Operator)).Append(' ');

            binaryExpressionAst.Right.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            WriteControlFlowStatement("break", breakStatementAst.Label);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitCommand(CommandAst commandAst)
        {
            if (commandAst.InvocationOperator != TokenKind.Unknown)
            {
                _sb.Append(GetTokenString(commandAst.InvocationOperator)).Append(' ');
            }

            Intersperse(commandAst.CommandElements, " ");

            if (commandAst.Redirections != null && commandAst.Redirections.Count > 0)
            {
                _sb.Append(' ');
                Intersperse(commandAst.Redirections, " ");
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            commandExpressionAst.Expression.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            _sb.Append('-');
            _sb.Append(commandParameterAst.ParameterName);

            if (commandParameterAst.Argument != null)
            {
                _sb.Append(':');
                commandParameterAst.Argument.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitConfigurationDefinition(ConfigurationDefinitionAst configurationDefinitionAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            if (constantExpressionAst.Value == null)
            {
                _sb.Append("$null");
            }
            else if (constantExpressionAst.StaticType == typeof(bool))
            {
                _sb.Append((bool)constantExpressionAst.Value ? "$true" : "$false");
            }
            else
            {
                _sb.Append(constantExpressionAst.Value);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            WriteControlFlowStatement("continue", continueStatementAst.Label);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            convertExpressionAst.Attribute.Visit(this);
            convertExpressionAst.Child.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitDataStatement(DataStatementAst dataStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            _sb.Append("do");
            doUntilStatementAst.Body.Visit(this);
            _sb.Append(" until (");
            doUntilStatementAst.Condition.Visit(this);
            _sb.Append(')');
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            _sb.Append("do");
            doWhileStatementAst.Body.Visit(this);
            _sb.Append(" while (");
            doWhileStatementAst.Condition.Visit(this);
            _sb.Append(')');
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitDynamicKeywordStatement(DynamicKeywordStatementAst dynamicKeywordStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            WriteControlFlowStatement("exit", exitStatementAst.Pipeline);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            _sb.Append('"').Append(expandableStringExpressionAst.Value).Append('"');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFileRedirection(FileRedirectionAst redirectionAst)
        {
            if (redirectionAst.FromStream != RedirectionStream.Output)
            {
                _sb.Append(GetStreamIndicator(redirectionAst.FromStream));
            }

            _sb.Append('>');

            redirectionAst.Location.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        private char GetStreamIndicator(RedirectionStream stream)
        {
            switch (stream)
            {
                case RedirectionStream.All:
                    return '*';

                case RedirectionStream.Debug:
                    return '5';

                case RedirectionStream.Error:
                    return '2';

                case RedirectionStream.Information:
                    return '6';

                case RedirectionStream.Output:
                    return '1';

                case RedirectionStream.Verbose:
                    return '4';

                case RedirectionStream.Warning:
                    return '3';

                default:
                    throw new ArgumentException($"Unknown redirection stream: '{stream}'");
            }
        }

        public override AstVisitAction VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            _sb.Append("foreach (");
            forEachStatementAst.Variable.Visit(this);
            _sb.Append(" in ");
            forEachStatementAst.Condition.Visit(this);
            _sb.Append(")");
            forEachStatementAst.Body.Visit(this);
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitForStatement(ForStatementAst forStatementAst)
        {
            _sb.Append("for (");
            forStatementAst.Initializer.Visit(this);
            _sb.Append("; ");
            forStatementAst.Condition.Visit(this);
            _sb.Append("; ");
            forStatementAst.Iterator.Visit(this);
            _sb.Append(')');
            forStatementAst.Body.Visit(this);
            EndStatement();
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            _sb.Append(functionDefinitionAst.IsFilter ? "filter " : "function ");
            _sb.Append(functionDefinitionAst.Name);
            Newline();
            functionDefinitionAst.Body.Visit(this);
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFunctionMember(FunctionMemberAst functionMemberAst)
        {
            if (!functionMemberAst.IsConstructor)
            {
                if (functionMemberAst.IsStatic)
                {
                    _sb.Append("static ");
                }

                if (functionMemberAst.IsHidden)
                {
                    _sb.Append("hidden ");
                }

                if (functionMemberAst.ReturnType != null)
                {
                    functionMemberAst.ReturnType.Visit(this);
                }
            }

            _sb.Append(functionMemberAst.Name).Append('(');
            WriteInlineParameters(functionMemberAst.Parameters);
            _sb.Append(')');
            Newline();

            functionMemberAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitHashtable(HashtableAst hashtableAst)
        {
            _sb.Append("@{");

            if (hashtableAst.KeyValuePairs == null || hashtableAst.KeyValuePairs.Count == 0)
            {
                _sb.Append('}');
                return AstVisitAction.SkipChildren;
            }

            Indent();
            WriteHashtableEntry(hashtableAst.KeyValuePairs[0]);
            for (int i = 1; i < hashtableAst.KeyValuePairs.Count; i++)
            {
                Newline();
                WriteHashtableEntry(hashtableAst.KeyValuePairs[i]);
            }
            Dedent();
            _sb.Append('}');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitIfStatement(IfStatementAst ifStmtAst)
        {
            _sb.Append("if (");
            ifStmtAst.Clauses[0].Item1.Visit(this);
            _sb.Append(')');
            ifStmtAst.Clauses[0].Item2.Visit(this);

            for (int i = 1; i < ifStmtAst.Clauses.Count; i++)
            {
                Newline();
                _sb.Append("elseif (");
                ifStmtAst.Clauses[i].Item1.Visit(this);
                _sb.Append(')');
                ifStmtAst.Clauses[i].Item2.Visit(this);
            }

            if (ifStmtAst.ElseClause != null)
            {
                Newline();
                _sb.Append("else");
                ifStmtAst.ElseClause.Visit(this);
            }

            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            indexExpressionAst.Target.Visit(this);
            _sb.Append('[');
            indexExpressionAst.Index.Visit(this);
            _sb.Append(']');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitInvokeMemberExpression(InvokeMemberExpressionAst methodCallAst)
        {
            methodCallAst.Expression.Visit(this);
            _sb.Append(methodCallAst.Static ? "::" : ".");
            methodCallAst.Member.Visit(this);
            _sb.Append('(');
            Intersperse(methodCallAst.Arguments, ", ");
            _sb.Append(')');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            memberExpressionAst.Expression.Visit(this);
            _sb.Append(memberExpressionAst.Static ? "::" : ".");
            memberExpressionAst.Member.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitMergingRedirection(MergingRedirectionAst redirectionAst)
        {
            _sb.Append(GetStreamIndicator(redirectionAst.FromStream))
                .Append(">&")
                .Append(GetStreamIndicator(redirectionAst.ToStream));

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
           _sb.Append(namedAttributeArgumentAst.ArgumentName);

            if (!namedAttributeArgumentAst.ExpressionOmitted && namedAttributeArgumentAst.Argument != null)
            {
                _sb.Append(" = ");
                namedAttributeArgumentAst.Argument.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            if (!namedBlockAst.Unnamed)
            {
                _sb.Append(GetTokenString(namedBlockAst.BlockKind)).Append(' ');
            }

            BeginBlock();

            WriteStatementBlock(namedBlockAst.Statements, namedBlockAst.Traps);

            EndBlock();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            _sb.Append("param(");

            if (paramBlockAst.Parameters == null || paramBlockAst.Parameters.Count == 0)
            {
                _sb.Append(')');
                return AstVisitAction.SkipChildren;
            }

            Indent();

            paramBlockAst.Parameters[0].Visit(this);

            for (int i = 1; i < paramBlockAst.Parameters.Count; i++)
            {
                _sb.Append(',');
                Newline(count: 2);
                paramBlockAst.Parameters[i].Visit(this);
            }

            Dedent();
            _sb.Append(')');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParameter(ParameterAst parameterAst)
        {
            if (parameterAst.Attributes != null && parameterAst.Attributes.Count > 0)
            {
                foreach (AttributeBaseAst attribute in parameterAst.Attributes)
                {
                    attribute.Visit(this);
                    Newline();
                }
            }

            parameterAst.Name.Visit(this);

            if (parameterAst.DefaultValue != null)
            {
                _sb.Append(" = ");
                parameterAst.DefaultValue.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            _sb.Append('(');
            parenExpressionAst.Pipeline.Visit(this);
            _sb.Append(')');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitPipeline(PipelineAst pipelineAst)
        {
            Intersperse(pipelineAst.PipelineElements, " | ");
#if PS7
            if (pipelineAst.Background)
            {
                _sb.Append(" &");
            }
#endif
            return AstVisitAction.SkipChildren;
        }

#if PS7
        public override AstVisitAction VisitPipelineChain(PipelineChainAst statementChain)
        {
            statementChain.LhsPipelineChain.Visit(this);
            _sb.Append(' ').Append(GetTokenString(statementChain.Operator)).Append(' ');
            statementChain.RhsPipeline.Visit(this);
            if (statementChain.Background)
            {
                _sb.Append(" &");
            }
            return AstVisitAction.SkipChildren;
        }
#endif

        public override AstVisitAction VisitPropertyMember(PropertyMemberAst propertyMemberAst)
        {
            if (propertyMemberAst.IsStatic)
            {
                _sb.Append("static ");
            }

            if (propertyMemberAst.IsHidden)
            {
                _sb.Append("hidden ");
            }

            if (propertyMemberAst.PropertyType != null)
            {
                propertyMemberAst.PropertyType.Visit(this);
            }

            _sb.Append('$').Append(propertyMemberAst.Name);

            if (propertyMemberAst.InitialValue != null)
            {
                _sb.Append(" = ");
                propertyMemberAst.InitialValue.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            WriteControlFlowStatement("return", returnStatementAst.Pipeline);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            _sb.Append('{');
            Indent();
            if (scriptBlockAst.ParamBlock != null)
            {
                scriptBlockAst.ParamBlock.Visit(this);
            }

            bool useExplicitEndBlock = false;

            if (scriptBlockAst.DynamicParamBlock != null)
            {
                Newline();
                scriptBlockAst.DynamicParamBlock.Visit(this);
            }

            if (scriptBlockAst.BeginBlock != null)
            {
                useExplicitEndBlock = true;
                Newline();
                scriptBlockAst.BeginBlock.Visit(this);
            }

            if (scriptBlockAst.ProcessBlock != null)
            {
                useExplicitEndBlock = true;
                Newline();
                scriptBlockAst.ProcessBlock.Visit(this);
            }

            if (useExplicitEndBlock)
            {
                scriptBlockAst.EndBlock.Visit(this);
            }
            else
            {
                WriteStatementBlock(scriptBlockAst.EndBlock.Statements, scriptBlockAst.EndBlock.Traps);
            }

            Dedent();
            _sb.Append('}');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            scriptBlockExpressionAst.ScriptBlock.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            BeginBlock();
            WriteStatementBlock(statementBlockAst.Statements, statementBlockAst.Traps);
            EndBlock();
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            switch (stringConstantExpressionAst.StringConstantType)
            {
                case StringConstantType.BareWord:
                    _sb.Append(stringConstantExpressionAst.Value);
                    break;

                case StringConstantType.SingleQuoted:
                    _sb.Append('\'').Append(stringConstantExpressionAst.Value.Replace("'", "''")).Append('\'');
                    break;

                case StringConstantType.DoubleQuoted:
                    WriteDoubleQuotedString(stringConstantExpressionAst.Value);
                    break;

                case StringConstantType.SingleQuotedHereString:
                    _sb.Append("@'\n").Append(stringConstantExpressionAst.Value).Append("\n'@");
                    break;

                case StringConstantType.DoubleQuotedHereString:
                    _sb.Append("@\"\n").Append(stringConstantExpressionAst.Value).Append("\n\"@");
                    break;

                default:
                    throw new ArgumentException($"Bad string contstant expression: '{stringConstantExpressionAst}' of type {stringConstantExpressionAst.StringConstantType}");
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            _sb.Append("$(");
            WriteStatementBlock(subExpressionAst.SubExpression.Statements, subExpressionAst.SubExpression.Traps);
            _sb.Append(')');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            throw new NotImplementedException();
        }

#if PS7
        public override AstVisitAction VisitTernaryExpression(TernaryExpressionAst ternaryExpressionAst)
        {
            ternaryExpressionAst.Condition.Visit(this);
            _sb.Append(" ? ");
            ternaryExpressionAst.IfTrue.Visit(this);
            _sb.Append(" : ");
            ternaryExpressionAst.IfFalse.Visit(this);
            return AstVisitAction.SkipChildren;
        }
#endif

        public override AstVisitAction VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            WriteControlFlowStatement("throw", throwStatementAst.Pipeline);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTrap(TrapStatementAst trapStatementAst)
        {
            _sb.Append("trap");

            if (trapStatementAst.TrapType != null)
            {
                _sb.Append(' ');
                trapStatementAst.TrapType.Visit(this);
            }

            trapStatementAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTryStatement(TryStatementAst tryStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            _sb.Append('[');
            WriteTypeName(typeConstraintAst.TypeName);
            _sb.Append(']');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTypeDefinition(TypeDefinitionAst typeDefinitionAst)
        {
            if (typeDefinitionAst.IsClass)
            {
                _sb.Append("class ");
            }
            else if (typeDefinitionAst.IsInterface)
            {
                _sb.Append("interface ");
            }
            else if (typeDefinitionAst.IsEnum)
            {
                _sb.Append("enum ");
            }
            else
            {
                throw new ArgumentException($"Unknown PowerShell type definition type: '{typeDefinitionAst}'");
            }

            _sb.Append(typeDefinitionAst.Name);

            if (typeDefinitionAst.BaseTypes != null && typeDefinitionAst.BaseTypes.Count > 0)
            {
                _sb.Append(" : ");

                WriteTypeName(typeDefinitionAst.BaseTypes[0].TypeName);

                for (int i = 1; i < typeDefinitionAst.BaseTypes.Count; i++)
                {
                    _sb.Append(_comma);
                    WriteTypeName(typeDefinitionAst.BaseTypes[i].TypeName);
                }
            }

            if (typeDefinitionAst.Members == null || typeDefinitionAst.Members.Count == 0)
            {
                Newline();
                _sb.Append('{');
                Newline();
                _sb.Append('}');

                return AstVisitAction.SkipChildren;
            }

            BeginBlock();

            if (typeDefinitionAst.IsEnum)
            {
                Intersperse(typeDefinitionAst.Members, "," + _newline);
            }
            else if (typeDefinitionAst.IsClass)
            {
                Intersperse(typeDefinitionAst.Members, _newline + _newline);
            }

            EndBlock();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            _sb.Append('[');
            WriteTypeName(typeExpressionAst.TypeName);
            _sb.Append(']');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            switch (unaryExpressionAst.TokenKind)
            {
                case TokenKind.PlusPlus:
                    _sb.Append("++");
                    unaryExpressionAst.Child.Visit(this);
                    break;

                case TokenKind.MinusMinus:
                    _sb.Append("--");
                    unaryExpressionAst.Child.Visit(this);
                    break;

                case TokenKind.PostfixPlusPlus:
                    unaryExpressionAst.Child.Visit(this);
                    _sb.Append("++");
                    break;

                case TokenKind.PostfixMinusMinus:
                    unaryExpressionAst.Child.Visit(this);
                    _sb.Append("--");
                    break;

                default:
                    _sb.Append(GetTokenString(unaryExpressionAst.TokenKind))
                        .Append(' ');
                    unaryExpressionAst.Child.Visit(this);
                    break;

            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            _sb.Append("$using:").Append(((VariableExpressionAst)usingExpressionAst.SubExpression).VariablePath.UserPath);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUsingStatement(UsingStatementAst usingStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            _sb.Append('$').Append(variableExpressionAst.VariablePath.UserPath);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            _sb.Append("while (");
            whileStatementAst.Condition.Visit(this);
            _sb.Append(")");
            whileStatementAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        private void WriteInlineParameters(IReadOnlyList<ParameterAst> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return;
            }

            WriteInlineParameter(parameters[0]);

            for (int i = 1; i < parameters.Count; i++)
            {
                WriteInlineParameter(parameters[i]);
            }
        }

        private void WriteInlineParameter(ParameterAst parameter)
        {
            foreach (AttributeBaseAst attribute in parameter.Attributes)
            {
                attribute.Visit(this);
            }
            parameter.Name.Visit(this);

            if (parameter.DefaultValue != null)
            {
                _sb.Append(" = ");
                parameter.DefaultValue.Visit(this);
            }
        }


        private void WriteControlFlowStatement(string keyword, Ast childAst)
        {
            _sb.Append(keyword);

            if (childAst != null)
            {
                _sb.Append(' ');
                childAst.Visit(this);
            }
        }

        private void WriteTypeName(ITypeName typeName)
        {
            switch (typeName)
            {
                case ArrayTypeName arrayTypeName:
                    WriteTypeName(arrayTypeName.ElementType);
                    if (arrayTypeName.Rank == 1)
                    {
                        _sb.Append("[]");
                    }
                    else
                    {
                        _sb.Append('[');
                        for (int i = 1; i < arrayTypeName.Rank; i++)
                        {
                            _sb.Append(',');
                        }
                        _sb.Append(']');
                    }
                    break;

                case GenericTypeName genericTypeName:
                    _sb.Append(genericTypeName.FullName)
                       .Append('[');

                    WriteTypeName(genericTypeName.GenericArguments[0]);
                    for (int i = 1; i < genericTypeName.GenericArguments.Count; i++)
                    {
                        _sb.Append(_comma);
                        WriteTypeName(genericTypeName.GenericArguments[i]);
                    }

                    _sb.Append(']');
                    break;

                case TypeName simpleTypeName:
                    _sb.Append(simpleTypeName.FullName);
                    break;

                default:
                    throw new ArgumentException($"Unknown type name type: '{typeName.GetType().FullName}'");
            }
        }

        private void WriteDoubleQuotedString(string strVal)
        {
            _sb.Append('"');

            foreach (char c in strVal)
            {
                switch (c)
                {
                    case '\0':
                        _sb.Append("`0");
                        break;

                    case '\a':
                        _sb.Append("`a");
                        break;

                    case '\b':
                        _sb.Append("`b");
                        break;

                    case '\f':
                        _sb.Append("`f");
                        break;

                    case '\n':
                        _sb.Append("`n");
                        break;

                    case '\r':
                        _sb.Append("`r");
                        break;

                    case '\t':
                        _sb.Append("`t");
                        break;

                    case '\v':
                        _sb.Append("`v");
                        break;

                    case '`':
                        _sb.Append("``");
                        break;

                    case '"':
                        _sb.Append("`\"");
                        break;

                    case '$':
                        _sb.Append("`$");
                        break;

                    case '\u001b':
                        _sb.Append("`e");
                        break;

                    default:
                        if (c < 128)
                        {
                            _sb.Append(c);
                            break;
                        }

                        _sb.Append("`u{").Append(((int)c).ToString("X")).Append('}');
                        break;
                }
            }

            _sb.Append('"');
        }

        private void WriteStatementBlock(IReadOnlyList<StatementAst> statements, IReadOnlyList<TrapStatementAst> traps = null)
        {
            bool wroteTrap = false;
            if (traps != null && traps.Count > 0)
            {
                wroteTrap = true;
                foreach (TrapStatementAst trap in traps)
                {
                    trap.Visit(this);
                }
            }

            if (statements != null && statements.Count > 0)
            {
                if (wroteTrap)
                {
                    EndStatement();
                }

                statements[0].Visit(this);

                for (int i = 1; i < statements.Count; i++)
                {
                    EndStatement();
                    statements[i].Visit(this);
                }
            }
        }

        private void WriteHashtableEntry(Tuple<ExpressionAst, StatementAst> entry)
        {
            entry.Item1.Visit(this);
            _sb.Append(" = ");
            entry.Item2.Visit(this);
        }

        private void BeginBlock()
        {
            Newline();
            _sb.Append('{');
            Indent();
        }

        private void EndBlock()
        {
            Dedent();
            _sb.Append('}');
        }

        private void Newline()
        {
            _sb.Append(_newline);

            for (int i = 0; i < _indent; i++)
            {
                _sb.Append(_indentStr);
            }
        }

        private void Newline(int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                _sb.Append(_newline);
            }

            Newline();
        }

        private void EndStatement()
        {
            _sb.Append(_newline);
        }

        private void Indent()
        {
            _indent++;
            Newline();
        }

        private void Dedent()
        {
            _indent--;
            Newline();
        }

        private void Intersperse(IReadOnlyList<Ast> asts, string separator)
        {
            if (asts == null || asts.Count == 0)
            {
                return;
            }

            asts[0].Visit(this);

            for (int i = 1; i < asts.Count; i++)
            {
                _sb.Append(separator);
                asts[i].Visit(this);
            }
        }

        private string GetTokenString(TokenKind tokenKind)
        {
            switch (tokenKind)
            {
                case TokenKind.Ampersand:
                    return "&";

                case TokenKind.And:
                    return "-and";

                case TokenKind.AndAnd:
                    return "&&";

                case TokenKind.As:
                    return "-as";

                case TokenKind.Assembly:
                    return "assembly";

                case TokenKind.AtCurly:
                    return "@{";

                case TokenKind.AtParen:
                    return "@(";

                case TokenKind.Band:
                    return "-band";

                case TokenKind.Base:
                    return "base";

                case TokenKind.Begin:
                    return "begin";

                case TokenKind.Bnot:
                    return "-bnot";

                case TokenKind.Bor:
                    return "-bnor";

                case TokenKind.Break:
                    return "break";

                case TokenKind.Bxor:
                    return "-bxor";

                case TokenKind.Catch:
                    return "catch";

                case TokenKind.Ccontains:
                    return "-ccontains";

                case TokenKind.Ceq:
                    return "-ceq";

                case TokenKind.Cge:
                    return "-cge";

                case TokenKind.Cgt:
                    return "-cgt";

                case TokenKind.Cin:
                    return "-cin";

                case TokenKind.Class:
                    return "class";

                case TokenKind.Cle:
                    return "-cle";

                case TokenKind.Clike:
                    return "-clike";

                case TokenKind.Clt:
                    return "-clt";

                case TokenKind.Cmatch:
                    return "-cmatch";

                case TokenKind.Cne:
                    return "-cne";

                case TokenKind.Cnotcontains:
                    return "-cnotcontains";

                case TokenKind.Cnotin:
                    return "-cnotin";

                case TokenKind.Cnotlike:
                    return "-cnotlike";

                case TokenKind.Cnotmatch:
                    return "-cnotmatch";

                case TokenKind.Colon:
                    return ":";

                case TokenKind.ColonColon:
                    return "::";

                case TokenKind.Comma:
                    return ",";

                case TokenKind.Configuration:
                    return "configuration";

                case TokenKind.Continue:
                    return "continue";

                case TokenKind.Creplace:
                    return "-creplace";

                case TokenKind.Csplit:
                    return "-csplit";

                case TokenKind.Data:
                    return "data";

                case TokenKind.Define:
                    return "define";

                case TokenKind.Divide:
                    return "/";

                case TokenKind.DivideEquals:
                    return "/=";

                case TokenKind.Do:
                    return "do";

                case TokenKind.DollarParen:
                    return "$(";

                case TokenKind.Dot:
                    return ".";

                case TokenKind.DotDot:
                    return "..";

                case TokenKind.Dynamicparam:
                    return "dynamicparam";

                case TokenKind.Else:
                    return "else";

                case TokenKind.ElseIf:
                    return "elseif";

                case TokenKind.End:
                    return "end";

                case TokenKind.Enum:
                    return "enum";

                case TokenKind.Equals:
                    return "=";

                case TokenKind.Exclaim:
                    return "!";

                case TokenKind.Exit:
                    return "exit";

                case TokenKind.Filter:
                    return "filter";

                case TokenKind.Finally:
                    return "finally";

                case TokenKind.For:
                    return "for";

                case TokenKind.Foreach:
                    return "foreach";

                case TokenKind.Format:
                    return "-f";

                case TokenKind.From:
                    return "from";

                case TokenKind.Function:
                    return "function";

                case TokenKind.Hidden:
                    return "hidden";

                case TokenKind.Icontains:
                    return "-contains";

                case TokenKind.Ieq:
                    return "-eq";

                case TokenKind.If:
                    return "if";

                case TokenKind.Ige:
                    return "-ge";

                case TokenKind.Igt:
                    return "-gt";

                case TokenKind.Iin:
                    return "-in";

                case TokenKind.Ile:
                    return "-le";

                case TokenKind.Ilike:
                    return "-like";

                case TokenKind.Ilt:
                    return "-lt";

                case TokenKind.Imatch:
                    return "-match";

                case TokenKind.In:
                    return "-in";

                case TokenKind.Ine:
                    return "-ne";

                case TokenKind.InlineScript:
                    return "inlinescript";

                case TokenKind.Inotcontains:
                    return "-notcontains";

                case TokenKind.Inotin:
                    return "-notin";

                case TokenKind.Inotlike:
                    return "-notlike";

                case TokenKind.Inotmatch:
                    return "-notmatch";

                case TokenKind.Interface:
                    return "interface";

                case TokenKind.Ireplace:
                    return "-replace";

                case TokenKind.Is:
                    return "-is";

                case TokenKind.IsNot:
                    return "-isnot";

                case TokenKind.Isplit:
                    return "-split";

                case TokenKind.Join:
                    return "-join";

                case TokenKind.LBracket:
                    return "[";

                case TokenKind.LCurly:
                    return "{";

                case TokenKind.LParen:
                    return "(";

                case TokenKind.Minus:
                    return "-";

                case TokenKind.MinusEquals:
                    return "-=";

                case TokenKind.MinusMinus:
                    return "--";

                case TokenKind.Module:
                    return "module";

                case TokenKind.Multiply:
                    return "*";

                case TokenKind.MultiplyEquals:
                    return "*=";

                case TokenKind.Namespace:
                    return "namespace";

                case TokenKind.NewLine:
                    return Environment.NewLine;

                case TokenKind.Not:
                    return "-not";

                case TokenKind.Or:
                    return "-or";

                case TokenKind.OrOr:
                    return "||";

                case TokenKind.Parallel:
                    return "parallel";

                case TokenKind.Param:
                    return "param";

                case TokenKind.Pipe:
                    return "|";

                case TokenKind.Plus:
                    return "+";

                case TokenKind.PlusEquals:
                    return "+=";

                case TokenKind.PlusPlus:
                    return "++";

                case TokenKind.PostfixMinusMinus:
                    return "--";

                case TokenKind.PostfixPlusPlus:
                    return "++";

                case TokenKind.Private:
                    return "private";

                case TokenKind.Process:
                    return "process";

                case TokenKind.Public:
                    return "public";

                case TokenKind.RBracket:
                    return "]";

                case TokenKind.RCurly:
                    return "}";

                case TokenKind.Rem:
                    return "%";

                case TokenKind.RemainderEquals:
                    return "%=";

                case TokenKind.Return:
                    return "return";

                case TokenKind.RParen:
                    return ")";

                case TokenKind.Semi:
                    return ";";

                case TokenKind.Sequence:
                    return "sequence";

                case TokenKind.Shl:
                    return "-shl";

                case TokenKind.Shr:
                    return "-shr";

                case TokenKind.Static:
                    return "static";

                case TokenKind.Switch:
                    return "switch";

                case TokenKind.Throw:
                    return "throw";

                case TokenKind.Trap:
                    return "trap";

                case TokenKind.Try:
                    return "try";

                case TokenKind.Until:
                    return "until";

                case TokenKind.Using:
                    return "using";

                case TokenKind.Var:
                    return "var";

                case TokenKind.While:
                    return "while";

                case TokenKind.Workflow:
                    return "workflow";

                case TokenKind.Xor:
                    return "-xor";

                default:
                    throw new ArgumentException($"Unable to stringify token kind '{tokenKind}'");
            }
        }
    }
}
