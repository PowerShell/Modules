using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation.Language;
using System.Text;

namespace Microsoft.PowerShell.AstTools
{
    public class StringPrettyPrinter : PrettyPrinter
    {
        private string _result;

        private StringWriter _sw;

        public string PrettyPrintInput(string input)
        {
            DoPrettyPrintInput(input);
            return _result;
        }

        public string PrettyPrintFile(string filePath)
        {
            DoPrettyPrintFile(filePath);
            return _result;
        }

        public string PrettyPrintAst(Ast ast, IReadOnlyList<Token> tokens)
        {
            DoPrettyPrintAst(ast, tokens);
            return _result;
        }

        protected override TextWriter CreateTextWriter()
        {
            _sw = new StringWriter();
            return _sw;
        }

        protected override void DoPostPrintAction()
        {
            _result = _sw.ToString();
        }
    }

    /// <summary>
    /// Prints a PowerShell AST based on its structure rather than text captured in extents.
    /// </summary>
    public abstract class PrettyPrinter
    {
        private readonly PrettyPrintingVisitor _visitor;

        /// <summary>
        /// Create a new pretty printer for use.
        /// </summary>
        protected PrettyPrinter()
        {
            _visitor = new PrettyPrintingVisitor();
        }

        protected abstract TextWriter CreateTextWriter();

        protected virtual void DoPostPrintAction()
        {
        }

        /// <summary>
        /// Pretty print a PowerShell script provided as an inline string.
        /// </summary>
        /// <param name="input">The inline PowerShell script to parse and pretty print.</param>
        /// <returns>A pretty-printed version of the given PowerShell script.</returns>
        protected void DoPrettyPrintInput(string input)
        {
            Ast ast = Parser.ParseInput(input, out Token[] tokens, out ParseError[] errors);

            if (errors != null && errors.Length > 0)
            {
                throw new ParseException(errors);
            }

            DoPrettyPrintAst(ast, tokens);
        }

        /// <summary>
        /// Pretty print the contents of a PowerShell file.
        /// </summary>
        /// <param name="filePath">The path of the PowerShell file to pretty print.</param>
        /// <returns>The pretty-printed file contents.</returns>
        protected void DoPrettyPrintFile(string filePath)
        {
            Ast ast = Parser.ParseFile(filePath, out Token[] tokens, out ParseError[] errors);

            if (errors != null && errors.Length > 0)
            {
                throw new ParseException(errors);
            }

            DoPrettyPrintAst(ast, tokens);
        }

        /// <summary>
        /// Pretty print a given PowerShell AST.
        /// </summary>
        /// <param name="ast">The PowerShell AST to print.</param>
        /// <param name="tokens">The token array generated when the AST was parsed. May be null.</param>
        /// <returns>The pretty-printed PowerShell AST in string form.</returns>
        protected void DoPrettyPrintAst(Ast ast, IReadOnlyList<Token> tokens)
        {
            using (TextWriter textWriter = CreateTextWriter())
            {
                _visitor.Run(textWriter, ast, tokens);
                DoPostPrintAction();
            }
        }
    }

    internal class PrettyPrintingVisitor : AstVisitor2
    {
        private TextWriter _tw;

        private readonly string _newline;

        private readonly string _indentStr;

        private readonly string _comma;

        private int _tokenIndex;

        private IReadOnlyList<Token> _tokens;

        private int _indent;

        public PrettyPrintingVisitor()
        {
            _newline = "\n";
            _indentStr = "    ";
            _comma = ", ";
            _indent = 0;
        }

        public void Run(
            TextWriter tw,
            Ast ast,
            IReadOnlyList<Token> tokens)
        {
            _tw = tw;
            _tokenIndex = 0;
            _tokens = tokens;
            ast.Visit(this);
            _tw = null;
        }

        public override AstVisitAction VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            WriteCommentsToAstPosition(arrayExpressionAst);

            _tw.Write("@(");
            WriteStatementBlock(arrayExpressionAst.SubExpression.Statements, arrayExpressionAst.SubExpression.Traps);
            _tw.Write(")");

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

            _tw.Write(' ');
            _tw.Write(GetTokenString(assignmentStatementAst.Operator));
            _tw.Write(' ');

            assignmentStatementAst.Right.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitAttribute(AttributeAst attributeAst)
        {
            WriteCommentsToAstPosition(attributeAst);

            _tw.Write('[');
            _tw.Write(attributeAst.TypeName);
            _tw.Write('(');

            bool hadPositionalArgs = false;
            if (!IsEmpty(attributeAst.PositionalArguments))
            {
                hadPositionalArgs = true;
                Intersperse(attributeAst.PositionalArguments, _comma);
            }

            if (!IsEmpty(attributeAst.NamedArguments))
            {
                if (hadPositionalArgs)
                {
                    _tw.Write(_comma);
                }

                Intersperse(attributeAst.NamedArguments, _comma);
            }

            _tw.Write(")]");
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
            WriteCommentsToAstPosition(baseCtorInvokeMemberExpressionAst);

            if (!IsEmpty(baseCtorInvokeMemberExpressionAst.Arguments))
            {
                _tw.Write("base(");
                Intersperse(baseCtorInvokeMemberExpressionAst.Arguments, ", ");
                _tw.Write(')');
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            binaryExpressionAst.Left.Visit(this);

            _tw.Write(' ');
            _tw.Write(GetTokenString(binaryExpressionAst.Operator));
            _tw.Write(' ');

            binaryExpressionAst.Right.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            throw new NotImplementedException();
        }

        public override AstVisitAction VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            WriteCommentsToAstPosition(breakStatementAst);
            WriteControlFlowStatement("break", breakStatementAst.Label);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            WriteCommentsToAstPosition(catchClauseAst);

            _tw.Write("catch");
            if (!IsEmpty(catchClauseAst.CatchTypes))
            {
                foreach (TypeConstraintAst typeConstraint in catchClauseAst.CatchTypes)
                {
                    _tw.Write(' ');
                    typeConstraint.Visit(this);
                }
            }

            catchClauseAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitCommand(CommandAst commandAst)
        {
            WriteCommentsToAstPosition(commandAst);

            if (commandAst.InvocationOperator != TokenKind.Unknown)
            {
                _tw.Write(GetTokenString(commandAst.InvocationOperator));
                _tw.Write(' ');
            }

            Intersperse(commandAst.CommandElements, " ");

            if (!IsEmpty(commandAst.Redirections))
            {
                _tw.Write(' ');
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
            WriteCommentsToAstPosition(commandParameterAst);

            _tw.Write('-');
            _tw.Write(commandParameterAst.ParameterName);

            if (commandParameterAst.Argument != null)
            {
                _tw.Write(':');
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
            WriteCommentsToAstPosition(constantExpressionAst);

            if (constantExpressionAst.Value == null)
            {
                _tw.Write("$null");
            }
            else if (constantExpressionAst.StaticType == typeof(bool))
            {
                _tw.Write((bool)constantExpressionAst.Value ? "$true" : "$false");
            }
            else
            {
                _tw.Write(constantExpressionAst.Value);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            WriteCommentsToAstPosition(continueStatementAst);
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
            WriteCommentsToAstPosition(doUntilStatementAst);
            _tw.Write("do");
            doUntilStatementAst.Body.Visit(this);
            _tw.Write(" until (");
            doUntilStatementAst.Condition.Visit(this);
            _tw.Write(')');
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            WriteCommentsToAstPosition(doWhileStatementAst);
            _tw.Write("do");
            doWhileStatementAst.Body.Visit(this);
            _tw.Write(" while (");
            doWhileStatementAst.Condition.Visit(this);
            _tw.Write(')');
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
            WriteCommentsToAstPosition(expandableStringExpressionAst);
            _tw.Write('"');
            _tw.Write(expandableStringExpressionAst.Value);
            _tw.Write('"');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFileRedirection(FileRedirectionAst redirectionAst)
        {
            WriteCommentsToAstPosition(redirectionAst);

            if (redirectionAst.FromStream != RedirectionStream.Output)
            {
                _tw.Write(GetStreamIndicator(redirectionAst.FromStream));
            }

            _tw.Write('>');

            redirectionAst.Location.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            WriteCommentsToAstPosition(forEachStatementAst);

            _tw.Write("foreach (");
            forEachStatementAst.Variable.Visit(this);
            _tw.Write(" in ");
            forEachStatementAst.Condition.Visit(this);
            _tw.Write(")");
            forEachStatementAst.Body.Visit(this);
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitForStatement(ForStatementAst forStatementAst)
        {
            WriteCommentsToAstPosition(forStatementAst);

            _tw.Write("for (");
            forStatementAst.Initializer.Visit(this);
            _tw.Write("; ");
            forStatementAst.Condition.Visit(this);
            _tw.Write("; ");
            forStatementAst.Iterator.Visit(this);
            _tw.Write(')');
            forStatementAst.Body.Visit(this);
            EndStatement();
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            WriteCommentsToAstPosition(functionDefinitionAst);

            _tw.Write(functionDefinitionAst.IsFilter ? "filter " : "function ");
            _tw.Write(functionDefinitionAst.Name);
            Newline();
            functionDefinitionAst.Body.Visit(this);
            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitFunctionMember(FunctionMemberAst functionMemberAst)
        {
            WriteCommentsToAstPosition(functionMemberAst);

            if (!functionMemberAst.IsConstructor)
            {
                if (functionMemberAst.IsStatic)
                {
                    _tw.Write("static ");
                }

                if (functionMemberAst.IsHidden)
                {
                    _tw.Write("hidden ");
                }

                if (functionMemberAst.ReturnType != null)
                {
                    functionMemberAst.ReturnType.Visit(this);
                }
            }

            _tw.Write(functionMemberAst.Name);
            _tw.Write('(');
            WriteInlineParameters(functionMemberAst.Parameters);
            _tw.Write(')');

            IReadOnlyList<StatementAst> statementAsts = functionMemberAst.Body.EndBlock.Statements;

            if (functionMemberAst.IsConstructor)
            {
                var baseCtorCall = (BaseCtorInvokeMemberExpressionAst)((CommandExpressionAst)functionMemberAst.Body.EndBlock.Statements[0]).Expression;

                if (!IsEmpty(baseCtorCall.Arguments))
                {
                    _tw.Write(" : ");
                    baseCtorCall.Visit(this);
                }

                var newStatementAsts = new StatementAst[functionMemberAst.Body.EndBlock.Statements.Count - 1];
                for (int i = 0; i < newStatementAsts.Length; i++)
                {
                    newStatementAsts[i] = functionMemberAst.Body.EndBlock.Statements[i + 1];
                }
                statementAsts = newStatementAsts;
            }

            if (IsEmpty(statementAsts) && IsEmpty(functionMemberAst.Body.EndBlock.Traps))
            {
                Newline();
                _tw.Write('{');
                Newline();
                _tw.Write('}');
                return AstVisitAction.SkipChildren;
            }

            BeginBlock();
            WriteStatementBlock(statementAsts, functionMemberAst.Body.EndBlock.Traps);
            EndBlock();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitHashtable(HashtableAst hashtableAst)
        {
            WriteCommentsToAstPosition(hashtableAst);

            _tw.Write("@{");

            if (IsEmpty(hashtableAst.KeyValuePairs))
            {
                _tw.Write('}');
                return AstVisitAction.SkipChildren;
            }

            Indent();

            Intersperse(
                hashtableAst.KeyValuePairs,
                WriteHashtableEntry,
                Newline);

            Dedent();
            _tw.Write('}');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitIfStatement(IfStatementAst ifStmtAst)
        {
            WriteCommentsToAstPosition(ifStmtAst);

            _tw.Write("if (");
            ifStmtAst.Clauses[0].Item1.Visit(this);
            _tw.Write(')');
            ifStmtAst.Clauses[0].Item2.Visit(this);

            for (int i = 1; i < ifStmtAst.Clauses.Count; i++)
            {
                Newline();
                _tw.Write("elseif (");
                ifStmtAst.Clauses[i].Item1.Visit(this);
                _tw.Write(')');
                ifStmtAst.Clauses[i].Item2.Visit(this);
            }

            if (ifStmtAst.ElseClause != null)
            {
                Newline();
                _tw.Write("else");
                ifStmtAst.ElseClause.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            WriteCommentsToAstPosition(indexExpressionAst);

            indexExpressionAst.Target.Visit(this);
            _tw.Write('[');
            indexExpressionAst.Index.Visit(this);
            _tw.Write(']');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitInvokeMemberExpression(InvokeMemberExpressionAst methodCallAst)
        {
            methodCallAst.Expression.Visit(this);
            _tw.Write(methodCallAst.Static ? "::" : ".");
            methodCallAst.Member.Visit(this);
            _tw.Write('(');
            Intersperse(methodCallAst.Arguments, ", ");
            _tw.Write(')');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            WriteCommentsToAstPosition(memberExpressionAst);

            memberExpressionAst.Expression.Visit(this);
            _tw.Write(memberExpressionAst.Static ? "::" : ".");
            memberExpressionAst.Member.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitMergingRedirection(MergingRedirectionAst redirectionAst)
        {
            WriteCommentsToAstPosition(redirectionAst);

            _tw.Write(GetStreamIndicator(redirectionAst.FromStream));
            _tw.Write(">&");
            _tw.Write(GetStreamIndicator(redirectionAst.ToStream));

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            WriteCommentsToAstPosition(namedAttributeArgumentAst);

           _tw.Write(namedAttributeArgumentAst.ArgumentName);

            if (!namedAttributeArgumentAst.ExpressionOmitted && namedAttributeArgumentAst.Argument != null)
            {
                _tw.Write(" = ");
                namedAttributeArgumentAst.Argument.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            WriteCommentsToAstPosition(namedBlockAst);

            if (!namedBlockAst.Unnamed)
            {
                _tw.Write(GetTokenString(namedBlockAst.BlockKind));
            }

            BeginBlock();

            WriteStatementBlock(namedBlockAst.Statements, namedBlockAst.Traps);

            EndBlock();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            WriteCommentsToAstPosition(paramBlockAst);

            if (!IsEmpty(paramBlockAst.Attributes))
            {
                foreach (AttributeAst attributeAst in paramBlockAst.Attributes)
                {
                    attributeAst.Visit(this);
                    Newline();
                }
            }

            _tw.Write("param(");

            if (IsEmpty(paramBlockAst.Parameters))
            {
                _tw.Write(')');
                return AstVisitAction.SkipChildren;
            }

            Indent();

            Intersperse(
                paramBlockAst.Parameters,
                () => { _tw.Write(','); Newline(count: 2); });

            Dedent();
            _tw.Write(')');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParameter(ParameterAst parameterAst)
        {
            WriteCommentsToAstPosition(parameterAst);

            if (!IsEmpty(parameterAst.Attributes))
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
                _tw.Write(" = ");
                parameterAst.DefaultValue.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            WriteCommentsToAstPosition(parenExpressionAst);
            _tw.Write('(');
            parenExpressionAst.Pipeline.Visit(this);
            _tw.Write(')');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitPipeline(PipelineAst pipelineAst)
        {
            WriteCommentsToAstPosition(pipelineAst);

            Intersperse(pipelineAst.PipelineElements, " | ");
#if PS7
            if (pipelineAst.Background)
            {
                _tw.Write(" &");
            }
#endif
            return AstVisitAction.SkipChildren;
        }

#if PS7
        public override AstVisitAction VisitPipelineChain(PipelineChainAst statementChain)
        {
            WriteCommentsToAstPosition(statementChain);
            statementChain.LhsPipelineChain.Visit(this);
            _tw.Write(' ');
            _tw.Write(GetTokenString(statementChain.Operator));
            _tw.Write(' ');
            statementChain.RhsPipeline.Visit(this);
            if (statementChain.Background)
            {
                _tw.Write(" &");
            }
            return AstVisitAction.SkipChildren;
        }
#endif

        public override AstVisitAction VisitPropertyMember(PropertyMemberAst propertyMemberAst)
        {
            WriteCommentsToAstPosition(propertyMemberAst);

            if (propertyMemberAst.IsStatic)
            {
                _tw.Write("static ");
            }

            if (propertyMemberAst.IsHidden)
            {
                _tw.Write("hidden ");
            }

            if (propertyMemberAst.PropertyType != null)
            {
                propertyMemberAst.PropertyType.Visit(this);
            }

            _tw.Write('$');
            _tw.Write(propertyMemberAst.Name);

            if (propertyMemberAst.InitialValue != null)
            {
                _tw.Write(" = ");
                propertyMemberAst.InitialValue.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            WriteCommentsToAstPosition(returnStatementAst);
            WriteControlFlowStatement("return", returnStatementAst.Pipeline);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            WriteCommentsToAstPosition(scriptBlockAst);

            if (scriptBlockAst.Parent != null)
            {
                _tw.Write('{');
                Indent();
            }

            bool needNewline = false;
            if (scriptBlockAst.ParamBlock != null)
            {
                needNewline = true;
                scriptBlockAst.ParamBlock.Visit(this);
            }

            Intersperse(scriptBlockAst.UsingStatements, Newline);

            bool useExplicitEndBlock = false;

            if (scriptBlockAst.DynamicParamBlock != null)
            {
                needNewline = useExplicitEndBlock = true;
                if (needNewline)
                {
                    Newline(count: 2);
                }

                scriptBlockAst.DynamicParamBlock.Visit(this);
            }

            if (scriptBlockAst.BeginBlock != null)
            {
                needNewline = useExplicitEndBlock = true;
                if (needNewline)
                {
                    Newline(count: 2);
                }

                scriptBlockAst.BeginBlock.Visit(this);
            }

            if (scriptBlockAst.ProcessBlock != null)
            {
                needNewline = useExplicitEndBlock = true;
                if (needNewline)
                {
                    Newline(count: 2);
                }

                scriptBlockAst.ProcessBlock.Visit(this);
            }

            if (scriptBlockAst.EndBlock != null
                && (!IsEmpty(scriptBlockAst.EndBlock.Statements) || !IsEmpty(scriptBlockAst.EndBlock.Traps)))
            {
                if (useExplicitEndBlock)
                {
                    Newline(count: 2);
                    scriptBlockAst.EndBlock.Visit(this);
                }
                else
                {
                    if (needNewline)
                    {
                        Newline(count: 2);
                    }

                    WriteStatementBlock(scriptBlockAst.EndBlock.Statements, scriptBlockAst.EndBlock.Traps);
                }
            }

            if (scriptBlockAst.Parent != null)
            {
                Dedent();
                _tw.Write('}');
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            scriptBlockExpressionAst.ScriptBlock.Visit(this);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            WriteCommentsToAstPosition(statementBlockAst);
            BeginBlock();
            WriteStatementBlock(statementBlockAst.Statements, statementBlockAst.Traps);
            EndBlock();
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            WriteCommentsToAstPosition(stringConstantExpressionAst);
            switch (stringConstantExpressionAst.StringConstantType)
            {
                case StringConstantType.BareWord:
                    _tw.Write(stringConstantExpressionAst.Value);
                    break;

                case StringConstantType.SingleQuoted:
                    _tw.Write('\'');
                    _tw.Write(stringConstantExpressionAst.Value.Replace("'", "''"));
                    _tw.Write('\'');
                    break;

                case StringConstantType.DoubleQuoted:
                    WriteDoubleQuotedString(stringConstantExpressionAst.Value);
                    break;

                case StringConstantType.SingleQuotedHereString:
                    _tw.Write("@'\n");
                    _tw.Write(stringConstantExpressionAst.Value);
                    _tw.Write("\n'@");
                    break;

                case StringConstantType.DoubleQuotedHereString:
                    _tw.Write("@\"\n");
                    _tw.Write(stringConstantExpressionAst.Value);
                    _tw.Write("\n\"@");
                    break;

                default:
                    throw new ArgumentException($"Bad string contstant expression: '{stringConstantExpressionAst}' of type {stringConstantExpressionAst.StringConstantType}");
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            WriteCommentsToAstPosition(subExpressionAst);
            _tw.Write("$(");
            WriteStatementBlock(subExpressionAst.SubExpression.Statements, subExpressionAst.SubExpression.Traps);
            _tw.Write(')');
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            WriteCommentsToAstPosition(switchStatementAst);

            if (switchStatementAst.Label != null)
            {
                _tw.Write(':');
                _tw.Write(switchStatementAst.Label);
                _tw.Write(' ');
            }

            _tw.Write("switch (");
            switchStatementAst.Condition.Visit(this);
            _tw.Write(')');

            BeginBlock();

            bool hasCases = false;
            if (!IsEmpty(switchStatementAst.Clauses))
            {
                hasCases = true;

                Intersperse(
                    switchStatementAst.Clauses,
                    (caseClause) => { caseClause.Item1.Visit(this); caseClause.Item2.Visit(this); },
                    () => Newline(count: 2));
            }

            if (switchStatementAst.Default != null)
            {
                if (hasCases)
                {
                    Newline(count: 2);
                }

                _tw.Write("default");
                switchStatementAst.Default.Visit(this);
            }

            EndBlock();

            return AstVisitAction.SkipChildren;
        }

#if PS7
        public override AstVisitAction VisitTernaryExpression(TernaryExpressionAst ternaryExpressionAst)
        {
            WriteCommentsToAstPosition(ternaryExpressionAst);

            ternaryExpressionAst.Condition.Visit(this);
            _tw.Write(" ? ");
            ternaryExpressionAst.IfTrue.Visit(this);
            _tw.Write(" : ");
            ternaryExpressionAst.IfFalse.Visit(this);
            return AstVisitAction.SkipChildren;
        }
#endif

        public override AstVisitAction VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            WriteCommentsToAstPosition(throwStatementAst);

            WriteControlFlowStatement("throw", throwStatementAst.Pipeline);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTrap(TrapStatementAst trapStatementAst)
        {
            WriteCommentsToAstPosition(trapStatementAst);

            _tw.Write("trap");

            if (trapStatementAst.TrapType != null)
            {
                _tw.Write(' ');
                trapStatementAst.TrapType.Visit(this);
            }

            trapStatementAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTryStatement(TryStatementAst tryStatementAst)
        {
            WriteCommentsToAstPosition(tryStatementAst);

            _tw.Write("try");
            tryStatementAst.Body.Visit(this);

            if (!IsEmpty(tryStatementAst.CatchClauses))
            {
                foreach (CatchClauseAst catchClause in tryStatementAst.CatchClauses)
                {
                    Newline();
                    catchClause.Visit(this);
                }
            }

            if (tryStatementAst.Finally != null)
            {
                Newline();
                _tw.Write("finally");
                tryStatementAst.Finally.Visit(this);
            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            WriteCommentsToAstPosition(typeConstraintAst);
            _tw.Write('[');
            WriteTypeName(typeConstraintAst.TypeName);
            _tw.Write(']');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTypeDefinition(TypeDefinitionAst typeDefinitionAst)
        {
            WriteCommentsToAstPosition(typeDefinitionAst);

            if (typeDefinitionAst.IsClass)
            {
                _tw.Write("class ");
            }
            else if (typeDefinitionAst.IsInterface)
            {
                _tw.Write("interface ");
            }
            else if (typeDefinitionAst.IsEnum)
            {
                _tw.Write("enum ");
            }
            else
            {
                throw new ArgumentException($"Unknown PowerShell type definition type: '{typeDefinitionAst}'");
            }

            _tw.Write(typeDefinitionAst.Name);

            if (!IsEmpty(typeDefinitionAst.BaseTypes))
            {
                _tw.Write(" : ");

                Intersperse(
                    typeDefinitionAst.BaseTypes,
                    (baseType) => WriteTypeName(baseType.TypeName),
                    () => _tw.Write(_comma));
            }

            if (IsEmpty(typeDefinitionAst.Members))
            {
                Newline();
                _tw.Write('{');
                Newline();
                _tw.Write('}');

                return AstVisitAction.SkipChildren;
            }

            BeginBlock();

            if (typeDefinitionAst.Members != null)
            {
                if (typeDefinitionAst.IsEnum)
                {
                    Intersperse(typeDefinitionAst.Members, () =>
                    {
                        _tw.Write(',');
                        Newline();
                    });
                }
                else if (typeDefinitionAst.IsClass)
                {
                    Intersperse(typeDefinitionAst.Members, () =>
                    {
                        Newline(count: 2);
                    });
                }
            }

            EndBlock();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            WriteCommentsToAstPosition(typeExpressionAst);
            _tw.Write('[');
            WriteTypeName(typeExpressionAst.TypeName);
            _tw.Write(']');

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            WriteCommentsToAstPosition(unaryExpressionAst);

            switch (unaryExpressionAst.TokenKind)
            {
                case TokenKind.PlusPlus:
                    _tw.Write("++");
                    unaryExpressionAst.Child.Visit(this);
                    break;

                case TokenKind.MinusMinus:
                    _tw.Write("--");
                    unaryExpressionAst.Child.Visit(this);
                    break;

                case TokenKind.PostfixPlusPlus:
                    unaryExpressionAst.Child.Visit(this);
                    _tw.Write("++");
                    break;

                case TokenKind.PostfixMinusMinus:
                    unaryExpressionAst.Child.Visit(this);
                    _tw.Write("--");
                    break;

                default:
                    _tw.Write(GetTokenString(unaryExpressionAst.TokenKind));
                    _tw.Write(' ');
                    unaryExpressionAst.Child.Visit(this);
                    break;

            }

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            WriteCommentsToAstPosition(usingExpressionAst);
            _tw.Write("$using:");
            _tw.Write(((VariableExpressionAst)usingExpressionAst.SubExpression).VariablePath.UserPath);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitUsingStatement(UsingStatementAst usingStatementAst)
        {
            WriteCommentsToAstPosition(usingStatementAst);

            _tw.Write("using ");

            switch (usingStatementAst.UsingStatementKind)
            {
                case UsingStatementKind.Assembly:
                    _tw.Write("assembly ");
                    break;

                case UsingStatementKind.Command:
                    _tw.Write("command ");
                    break;

                case UsingStatementKind.Module:
                    _tw.Write("module ");
                    break;

                case UsingStatementKind.Namespace:
                    _tw.Write("namespace ");
                    break;

                case UsingStatementKind.Type:
                    _tw.Write("type ");
                    break;

                default:
                    throw new ArgumentException($"Unknown using statement kind: '{usingStatementAst.UsingStatementKind}'");
            }

            if (usingStatementAst.ModuleSpecification != null)
            {
                _tw.Write("@{ ");

                Intersperse(
                    usingStatementAst.ModuleSpecification.KeyValuePairs,
                    (kvp) =>
                    {
                        WriteCommentsToAstPosition(kvp.Item1);
                        kvp.Item1.Visit(this);
                        _tw.Write(" = ");
                        WriteCommentsToAstPosition(kvp.Item2);
                        kvp.Item2.Visit(this);
                    },
                    () => { _tw.Write("; "); });

                _tw.Write(" }");
                EndStatement();

                return AstVisitAction.SkipChildren;
            }

            if (usingStatementAst.Name != null)
            {
                usingStatementAst.Name.Visit(this);
            }

            if (usingStatementAst.Alias != null)
            {
                _tw.Write(" = ");
                usingStatementAst.Alias.Visit(this);
            }

            EndStatement();

            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            WriteCommentsToAstPosition(variableExpressionAst);
            _tw.Write(variableExpressionAst.Splatted ? '@' : '$');
            _tw.Write(variableExpressionAst.VariablePath.UserPath);
            return AstVisitAction.SkipChildren;
        }

        public override AstVisitAction VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            WriteCommentsToAstPosition(whileStatementAst);
            _tw.Write("while (");
            whileStatementAst.Condition.Visit(this);
            _tw.Write(")");
            whileStatementAst.Body.Visit(this);

            return AstVisitAction.SkipChildren;
        }

        private void WriteInlineParameters(IReadOnlyList<ParameterAst> parameters)
        {
            if (IsEmpty(parameters))
            {
                return;
            }

            foreach (ParameterAst parameterAst in parameters)
            {
                WriteInlineParameter(parameterAst);
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
                _tw.Write(" = ");
                parameter.DefaultValue.Visit(this);
            }
        }


        private void WriteControlFlowStatement(string keyword, Ast childAst)
        {
            _tw.Write(keyword);

            if (childAst != null)
            {
                _tw.Write(' ');
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
                        _tw.Write("[]");
                    }
                    else
                    {
                        _tw.Write('[');
                        for (int i = 1; i < arrayTypeName.Rank; i++)
                        {
                            _tw.Write(',');
                        }
                        _tw.Write(']');
                    }
                    break;

                case GenericTypeName genericTypeName:
                    _tw.Write(genericTypeName.FullName);
                    _tw.Write('[');

                    Intersperse(
                        genericTypeName.GenericArguments,
                        (gtn) => WriteTypeName(gtn),
                        () => _tw.Write(_comma));

                    _tw.Write(']');
                    break;

                case TypeName simpleTypeName:
                    _tw.Write(simpleTypeName.FullName);
                    break;

                default:
                    throw new ArgumentException($"Unknown type name type: '{typeName.GetType().FullName}'");
            }
        }

        private void WriteDoubleQuotedString(string strVal)
        {
            _tw.Write('"');

            foreach (char c in strVal)
            {
                switch (c)
                {
                    case '\0':
                        _tw.Write("`0");
                        break;

                    case '\a':
                        _tw.Write("`a");
                        break;

                    case '\b':
                        _tw.Write("`b");
                        break;

                    case '\f':
                        _tw.Write("`f");
                        break;

                    case '\n':
                        _tw.Write("`n");
                        break;

                    case '\r':
                        _tw.Write("`r");
                        break;

                    case '\t':
                        _tw.Write("`t");
                        break;

                    case '\v':
                        _tw.Write("`v");
                        break;

                    case '`':
                        _tw.Write("``");
                        break;

                    case '"':
                        _tw.Write("`\"");
                        break;

                    case '$':
                        _tw.Write("`$");
                        break;

                    case '\u001b':
                        _tw.Write("`e");
                        break;

                    default:
                        if (c < 128)
                        {
                            _tw.Write(c);
                            break;
                        }

                        _tw.Write("`u{");
                        _tw.Write(((int)c).ToString("X"));
                        _tw.Write('}');
                        break;
                }
            }

            _tw.Write('"');
        }

        private void WriteStatementBlock(IReadOnlyList<StatementAst> statements, IReadOnlyList<TrapStatementAst> traps = null)
        {
            bool wroteTrap = false;
            if (!IsEmpty(traps))
            {
                wroteTrap = true;
                foreach (TrapStatementAst trap in traps)
                {
                    trap.Visit(this);
                }
            }

            if (!IsEmpty(statements))
            {
                if (wroteTrap)
                {
                    Newline();
                }

                statements[0].Visit(this);
                StatementAst previousStatement = statements[0];

                for (int i = 1; i < statements.Count; i++)
                {
                    if (IsBlockStatement(previousStatement))
                    {
                        _tw.Write(_newline);
                    }
                    Newline();
                    statements[i].Visit(this);
                    previousStatement = statements[i];
                }
            }
        }

        private void WriteHashtableEntry(Tuple<ExpressionAst, StatementAst> entry)
        {
            entry.Item1.Visit(this);
            _tw.Write(" = ");
            entry.Item2.Visit(this);
        }

        private void BeginBlock()
        {
            Newline();
            _tw.Write('{');
            Indent();
        }

        private void EndBlock()
        {
            Dedent();
            _tw.Write('}');
        }

        private void Newline()
        {
            _tw.Write(_newline);

            for (int i = 0; i < _indent; i++)
            {
                _tw.Write(_indentStr);
            }
        }

        private void Newline(int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                _tw.Write(_newline);
            }

            Newline();
        }

        private void EndStatement()
        {
            _tw.Write(_newline);
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
            if (IsEmpty(asts))
            {
                return;
            }

            asts[0].Visit(this);

            for (int i = 1; i < asts.Count; i++)
            {
                _tw.Write(separator);
                asts[i].Visit(this);
            }
        }

        private void Intersperse(IReadOnlyList<Ast> asts, Action writeSeparator)
        {
            if (IsEmpty(asts))
            {
                return;
            }

            asts[0].Visit(this);

            for (int i = 1; i < asts.Count; i++)
            {
                writeSeparator();
                asts[i].Visit(this);
            }
        }

        private void Intersperse<T>(IReadOnlyList<T> astObjects, Action<T> writeObject, Action writeSeparator)
        {
            if (IsEmpty(astObjects))
            {
                return;
            }

            writeObject(astObjects[0]);

            for (int i = 1; i < astObjects.Count; i++)
            {
                writeSeparator();
                writeObject(astObjects[i]);
            }
        }

        private void WriteCommentsToAstPosition(Ast ast)
        {
            if (_tokens == null)
            {
                return;
            }

            Token currToken = _tokens[_tokenIndex];
            while (currToken.Extent.EndOffset < ast.Extent.StartOffset)
            {
                if (currToken.Kind == TokenKind.Comment)
                {
                    _tw.Write(currToken.Text);

                    if (currToken.Text.StartsWith("#"))
                    {
                        Newline();
                    }
                }

                _tokenIndex++;
                currToken = _tokens[_tokenIndex];
            }
        }

        private bool IsBlockStatement(StatementAst statementAst)
        {
            switch (statementAst)
            {
                case PipelineBaseAst _:
                case ReturnStatementAst _:
                case ThrowStatementAst _:
                case ExitStatementAst _:
                case BreakStatementAst _:
                case ContinueStatementAst _:
                    return false;

                default:
                    return true;
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

        private static bool IsEmpty<T>(IReadOnlyCollection<T> collection)
        {
            return collection == null
                || collection.Count == 0;
        }
    }

    public class ParseException : Exception
    {
        public ParseException(IReadOnlyList<ParseError> parseErrors)
            : base("A parse error was encountered while parsing the input script")
        {
            ParseErrors = parseErrors;
        }

        public IReadOnlyList<ParseError> ParseErrors { get; }
    }
}
