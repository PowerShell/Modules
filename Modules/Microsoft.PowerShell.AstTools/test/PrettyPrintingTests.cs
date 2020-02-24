using System;
using System.IO;
using System.Management.Automation.Language;
using Microsoft.PowerShell.PrettyPrinter;
using Xunit;

namespace test
{
    public class PrettyPrinterTests
    {
        private readonly PrettyPrinter _pp;

        public PrettyPrinterTests()
        {
            _pp = new PrettyPrinter();
        }

        [Theory()]
        [InlineData("$x")]
        [InlineData("- $x")]
        [InlineData("- 1")]
        [InlineData("-1")]
        [InlineData("$x++")]
        [InlineData("$i--")]
        [InlineData("--$i")]
        [InlineData("++$i")]
        [InlineData("- --$i")]
        [InlineData("-not $true")]
        [InlineData("$x + $y")]
        [InlineData("'{0}' -f 'Hi'")]
        [InlineData("'1,2,3' -split ','")]
        [InlineData("1, 2, 3 -join ' '")]
        [InlineData("Get-ChildItem")]
        [InlineData("gci >test.txt")]
        [InlineData("gci >test.txt 2>errs.txt")]
        [InlineData("gci 2>&1")]
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
        [InlineData("Invoke-Expression 'runCommand' &")]
        [InlineData(@"""I`e[31mlike`e[0mducks""")]
        [InlineData("1 && 2")]
        [InlineData("sudo apt update && sudo apt upgrade")]
        [InlineData("firstthing && secondthing &")]
        [InlineData("Get-Item ./thing || $(throw 'Bad')")]
        [InlineData(@"$true ? 'true' : 'false'")]
#endif
        public void TestPrettyPrintingIdempotentForSimpleStatements(string input)
        {
            AssertPrettyPrintedStatementIdentical(input);
        }

        [Fact]
        public void TestEmptyHashtable()
        {
            string script = "@{}";
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestSimpleHashtable()
        {
            string script = @"
@{
    One = 'One'
    Two = $x
    $banana = 7
}
";
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestComplexHashtable()
        {
            string script = @"
@{
    One = @{
        SubOne = 1
        SubTwo = {
            $x
        }
    }
    Two = $x
    $banana = @(7, 3, 4)
}
";
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestFunction()
        {
            string script = @"
function Test-Function
{
    Write-Host 'Hello!'
}
";
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestAdvancedFunction()
        {
            string script = @"
function Test-Greeting
{
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]
        $Greeting
    )

    Write-Host $Greeting
}
";
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlock()
        {
            string script = @"
{
    $args[0] + 2
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockInvocation()
        {
            string script = @"
& {
    $args[0] + 2
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockDotSource()
        {
            string script = @"
. {
    $args[0] + 2
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockEmptyParams()
        {
            string script = @"
{
    param()
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockParams()
        {
            string script = @"
{
    param(
        $String,

        $Switch
    )
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockAttributedParams()
        {
            string script = @"
{
    param(
        [Parameter()]
        [string]
        $String,

        [Parameter()]
        [switch]
        $Switch
    )
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestScriptBlockParamAttributesWithArguments()
        {
            string script = @"
{
    param(
        [Parameter(Mandatory)]
        [string]
        $String,

        [Parameter(Mandatory = $true)]
        [AnotherAttribute(1, 2)]
        [ThirdAttribute(1, 2, Fun = $true)]
        [ThirdAttribute(1, 2, Fun)]
        [switch]
        $Switch
    )
}";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestWhileLoop()
        {
            string script = @"
while ($i -lt 10)
{
    $i++
}
";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestForeachLoop()
        {
            string script = @"
foreach ($n in 1, 2, 3)
{
    Write-Output ($n + 1)
}
";

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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
            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestDoWhileLoop()
        {
            string script = @"
do
{
    $x++
} while ($x -lt 10)
";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestDoUntilLoop()
        {
            string script = @"
do
{
    $x++
} until ($x -eq 10)
";

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestSimpleClass()
        {
            string script = @"
class Duck
{
}
";

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
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

            AssertPrettyPrintedStatementIdentical(script);
        }


        [Fact]
        public void TestClassConstructorWithBaseClass()
        {
            string script = @"
class MyHashtable : hashtable
{
    MyHashtable([int]$count) : base($count)
    {
    }
}
";

            AssertPrettyPrintedStatementIdentical(script);
        }

        [Fact]
        public void TestUsingNamespace()
        {
            string script = "using namespace System.Collections.Generic\n";
            AssertPrettyPrintedUsingStatementIdentical(script);
        }

        [Fact]
        public void TestUsingModule()
        {
            string script = "using module PrettyPrintingTestModule\n";
            using (ModuleContext.Create("PrettyPrintingTestModule", new Version(1, 0)))
            {
                AssertPrettyPrintedUsingStatementIdentical(script);
            }
        }

        [Fact]
        public void TestUsingModuleWithHashtable()
        {
            string script = "using module @{ ModuleName = 'PrettyPrintingTestModule'; ModuleVersion = '1.18' }\n";
            using (ModuleContext.Create("PrettyPrintingTestModule", new Version(1, 18)))
            {
                AssertPrettyPrintedUsingStatementIdentical(script);
            }
        }

        [Fact]
        public void TestFullScript1()
        {
            string script = @"
[CmdletBinding(DefaultParameterSetName = ""BuildOne"")]
param(
    [Parameter(ParameterSetName = ""BuildAll"")]
    [switch]
    $All,

    [Parameter(ParameterSetName = ""BuildOne"")]
    [ValidateRange(3, 7)]
    [int]
    $PSVersion = $PSVersionTable.PSVersion.Major,

    [Parameter(ParameterSetName = ""BuildOne"")]
    [Parameter(ParameterSetName = ""BuildAll"")]
    [ValidateSet(""Debug"", ""Release"")]
    [string]
    $Configuration = ""Debug"",

    [Parameter(ParameterSetName = ""BuildDocumentation"")]
    [switch]
    $Documentation,

    [Parameter(ParameterSetName = 'BuildAll')]
    [Parameter(ParameterSetName = 'BuildOne')]
    [switch]
    $Clobber,

    [Parameter(Mandatory = $true, ParameterSetName = 'Clean')]
    [switch]
    $Clean,

    [Parameter(Mandatory = $true, ParameterSetName = 'Test')]
    [switch]
    $Test,

    [Parameter(ParameterSetName = 'Test')]
    [switch]
    $InProcess,

    [Parameter(ParameterSetName = 'Bootstrap')]
    [switch]
    $Bootstrap
)

begin
{
    if ($PSVersion -gt 6)
    {
        Write-Host ""Building PowerShell Core version""
        $PSVersion = 6
    }
}

end
{
    Import-Module -Force (Join-Path $PSScriptRoot build.psm1)
    if ($Clean -or $Clobber)
    {
        Remove-Build
        if ($PSCmdlet.ParameterSetName -eq ""Clean"")
        {
            return
        }
    }

    $setName = $PSCmdlet.ParameterSetName
    switch ($setName)
    {
        ""BuildAll""
        {
            Start-ScriptAnalyzerBuild -All -Configuration $Configuration
        }

        ""BuildDocumentation""
        {
            Start-ScriptAnalyzerBuild -Documentation
        }

        ""BuildOne""
        {
            $buildArgs = @{
                PSVersion = $PSVersion
                Configuration = $Configuration
            }
            Start-ScriptAnalyzerBuild @buildArgs
        }

        ""Bootstrap""
        {
            Install-DotNet
            return
        }

        ""Test""
        {
            Test-ScriptAnalyzer -InProcess:$InProcess
            return
        }

        default
        {
            throw ""Unexpected parameter set '$setName'""
        }
    }
}
";

            AssertPrettyPrintedScriptIdentical(script);
        }

        private void AssertPrettyPrintedStatementIdentical(string input)
        {
            Assert.Equal(NormalizeScript(input), NormalizeScript(_pp.PrettyPrintInput(input)));
        }

        private void AssertPrettyPrintedUsingStatementIdentical(string input)
        {
            Assert.Equal(NormalizeScript(input), NormalizeScript(_pp.PrettyPrintInput(input)));
        }

        private void AssertPrettyPrintedScriptIdentical(string input)
        {
            Assert.Equal(NormalizeScript(input), NormalizeScript(_pp.PrettyPrintInput(input)));
        }

        private static string NormalizeScript(string input)
        {
            return input.Trim().Replace(Environment.NewLine, "\n");
        }
    }

    internal class ModuleContext : IDisposable
    {
        public static ModuleContext Create(string moduleName, Version moduleVersion)
        {
            string tmpDirPath = Path.GetTempPath();
            Directory.CreateDirectory(tmpDirPath);
            string modulePath = Path.Combine(tmpDirPath, moduleName);
            Directory.CreateDirectory(modulePath);
            string manifestPath = Path.Combine(modulePath, $"{moduleName}.psd1");
            File.WriteAllText(manifestPath, $"@{{ ModuleVersion = '{moduleVersion}' }}");

            string oldPSModulePath = Environment.GetEnvironmentVariable("PSModulePath");
            Environment.SetEnvironmentVariable("PSModulePath", tmpDirPath);

            return new ModuleContext(modulePath, oldPSModulePath);
        }

        private readonly string _psModulePath;

        private readonly string _modulePath;

        public ModuleContext(
            string modulePath,
            string psModulePath)
        {
            _modulePath = modulePath;
            _psModulePath = psModulePath;
        }

        public void Dispose()
        {
            Directory.Delete(_modulePath, recursive: true);
            Environment.SetEnvironmentVariable("PSModulePath", _psModulePath);
        }
    }
}
