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
        [InlineData("- $x")]
        [InlineData("- 1")]
        [InlineData("-1")]
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
        [InlineData(@"$x[0]")]
        [InlineData(@"$x[$i + 1]")]
        [InlineData(@"$x.Item[$i + 1]")]
        [InlineData(@"1, 2, 3")]
        [InlineData(@"1, 'Hi', 3")]
        [InlineData(@"@(1, 'Hi', 3)")]
#if PS7
        [InlineData(@"""I`e[31mlike`e[0mducks""")]
        [InlineData("1 && 2")]
        [InlineData("sudo apt update && sudo apt upgrade")]
        [InlineData("Get-Item ./thing || $(throw 'Bad')")]
#endif
        public void TestPrettyPrintingIdempotentForSimpleStatements(string input)
        {
            AssertPrettyPrintingIdentical(input);
        }

        [Fact]
        public void TestWhileLoop()
        {
            string script = @"
$i = 0
while ($i -lt 10)
{
    $i++
}
Write-Host ""`$i = $i""
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestForeachLoop()
        {
            string script = @"
foreach ($n in 1,2,3)
{
    Write-Output ($n + 1)
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestForLoop()
        {
            string script = @"
for ($i = 0; $i -lt $args.Count; $i++)
{
    $args[$i]
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestSwitchCase()
        {
            string script = @"
switch ($x)
{
    1
    {
        'One'
        break
    }

    2
    {
        'Two'
        break
    }

    3
    {
        'Three'
        break
    }
}
";
            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestDoWhileLoop()
        {
            string script = @"
$x = 0
do
{
    $x++
} while ($x -lt 10)
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestDoUntilLoop()
        {
            string script = @"
$x = 0
do
{
    $x++
} until ($x -eq 10)
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestIf()
        {
            string script = @"
if ($x)
{
    $x.Fun()
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestElseIf()
        {
            string script = @"
if ($x)
{
    $x.Fun()
}
elseif ($y)
{
    $y.NoFun()
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestElse()
        {
            string script = @"
if ($x)
{
    $x.Fun()
}
else
{
    'nothing'
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestFullIfElse()
        {
            string script = @"
if ($x)
{
    $x.Fun()
}
elseif ($y)
{
    $y.NoFun()
}
else
{
    'nothing'
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestTryCatch()
        {
            string script = @"
try
{
    Write-Error 'Bad' -ErrorAction Stop
}
catch
{
    $_
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestTryCatchWithType()
        {
            string script = @"
try
{
    Write-Error 'Bad' -ErrorAction Stop
}
catch [System.Exception]
{
    $_
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestTryFinally()
        {
            string script = @"
try
{
    Write-Error 'Bad' -ErrorAction Stop
}
finally
{
    Write-Host 'done'
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestTryCatchFinally()
        {
            string script = @"
try
{
    Write-Error 'Bad' -ErrorAction Stop
}
catch
{
    $_
}
finally
{
    Write-Host 'done'
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestSimpleClass()
        {
            string script = @"
class Duck
{
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestClassProperty()
        {
            string script = @"
class Duck
{
    [string]$Name
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestClassMethod()
        {
            string script = @"
class Duck
{
    [string]GetGreeting([string]$Name)
    {
        return ""Hi $Name""
    }
}
";

            AssertPrettyPrintingIdentical(script);
        }

        [Fact]
        public void TestClassConstructor()
        {
            string script = @"
class Duck
{
    Duck($name)
    {
        $this.Name = $name
    }
}
";

            AssertPrettyPrintingIdentical(script);
        }


        [Fact]
        public void TestClassConstructorWithBaseClass()
        {
            string script = @"
class Duck : object
{
    Duck() : base()
    {
    }
}
";

            AssertPrettyPrintingIdentical(script);
        }
        public void AssertPrettyPrintingIdentical(string input)
        {
            Ast ast = Parser.ParseInput(input, out Token[] _, out ParseError[] _);
            StatementAst statementAst = ((ScriptBlockAst)ast).EndBlock.Statements[0];
            Assert.Equal(input, PrettyPrinter.PrettyPrint(statementAst));
        }
    }
}
