module Server.Tests

open Expecto

open Shared
open Server

let server =
    testList "Server" [
        testCaseAsync "Topas API config stub returns Ok"
        <| async {
            let api = topasApi null
            let! result = api.getAppConfig ()

            Expect.isOk result "Config stub should succeed"
        }
    ]

let all = testList "All" [ Shared.Tests.shared; server ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all
