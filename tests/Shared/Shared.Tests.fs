module Shared.Tests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Shared

let shared =
    testList "Shared" [
        testCase "Generate request DTO keeps selected node digits"
        <| fun _ ->
            let dto = {
                SelectedTemplatePaths = [ "physics/em_standard_opt4.txt" ]
                SelectedNodeDigits = [ "1"; "2" ]
                SelectedPhaseSpaceIndexes = [ "01" ]
            }

            Expect.equal dto.SelectedNodeDigits.Length 2 "Should keep provided node digits"
    ]