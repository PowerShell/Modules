Describe "Test Microsoft.PowerShell.SecretsManagement" -tags CI {
    BeforeAll {
    }
    BeforeEach {
    }
    AfterEach {
    }
    AfterAll {
    }
    It "This is the first test for Microsoft.PowerShell.SecretsManagement" {
        $name = "Hello World"
        verb-noun -name $name | Should -BeExactly $name
    }
}
