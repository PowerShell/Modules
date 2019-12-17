Describe "Test Microsoft.PSRemotingTools" -tags CI {
    BeforeAll {
    }
    BeforeEach {
    }
    AfterEach {
    }
    AfterAll {
    }
    It "This is the first test for Microsoft.PSRemotingTools" {
        $name = "Hello World"
        verb-noun -name $name | Should -BeExactly $name
    }
}
