using System;
using System.Management.Automation.Language;
using Microsoft.PowerShell.PrettyPrinter;
using Xunit;

namespace test
{
    public class PrettyPrinterTests
    {
        [Theory()]
        [InlineData("$x")]
        [InlineData("$x + $y")]
        [InlineData("Get-ChildItem")]
        [InlineData("Get-ChildItem -Recurse -Path ./here")]
        [InlineData("Get-ChildItem -Recurse -Path \"$PWD\\there\"")]
        [InlineData("exit 1")]
        [InlineData("return ($result + 3)")]
        [InlineData("throw [System.Exception]'Bad'")]
        [InlineData("break outer")]
        [InlineData("continue anotherLoop")]
        [InlineData("3 + $(Get-Random)")]
        [InlineData("'banana cake'")]
        [InlineData("'banana''s cake'")]
        [InlineData("Get-ChildItem | ? Name -like 'banana' | % FullPath")]
        [InlineData("[type]::GetThings()")]
        [InlineData("[type]::Member")]
        [InlineData("$x.DoThings($x, 8, $y)")]
        [InlineData("$x.Property")]
        [InlineData("$x.$property")]
        [InlineData("[type]::$property")]
        [InlineData("$type::$property")]
        [InlineData("[type]::$method(1, 2, 'x')")]
        [InlineData("$k.$method(1, 2, 'x')")]
        [InlineData(@"""I like ducks""")]
        [InlineData(@"""I`nlike`nducks""")]
        [InlineData(@"""I`tlike`n`rducks""")]
#if PS7
        [InlineData(@"""I`e[31mlike`e[0mducks""")]
        [InlineData("1 && 2")]
        [InlineData("sudo apt update && sudo apt upgrade")]
        [InlineData("Get-Item ./thing || $(throw 'Bad')")]
#endif
        public void TestPrettyPrintingIdempotentForSimpleStatements(string input)
        {
            Ast ast = Parser.ParseInput(input, out Token[] _, out ParseError[] _);
            StatementAst statementAst = ((ScriptBlockAst)ast).EndBlock.Statements[0];
            Assert.Equal(input, PrettyPrinter.PrettyPrint(statementAst));
        }
    }
}
