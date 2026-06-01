open System
open System.Collections.Generic
open System.Net.Http
open Suave
open Suave.Filters
open Suave.Operators
open Suave.RequestErrors
open Newtonsoft.Json



let jsonResponse (statusCode: HttpCode) (obj: obj) : WebPart =
    let body = JsonConvert.SerializeObject(obj)
    Writers.setMimeType "application/json; charset=utf-8"
    >=> Suave.Response.response statusCode (Text.Encoding.UTF8.GetBytes body)



let processedIds = HashSet<string>()

let isAlreadyProcessed (id: string) =
    lock processedIds (fun () ->
        if processedIds.Contains(id) then
            true
        else
            processedIds.Add(id) |> ignore
            false
    )



let SECRET_TOKEN = "meu-token-secreto"
let confirmUrl   = "http://127.0.0.1:5001/confirmar"
let cancelUrl    = "http://127.0.0.1:5001/cancelar"



let httpClient = new HttpClient()

let postToGateway (url: string) (transactionId: string) =
    async {
        let body    = sprintf """{"transaction_id": "%s"}""" transactionId
        let content = new StringContent(body, Text.Encoding.UTF8, "application/json")
        try
            let! _ = httpClient.PostAsync(url, content) |> Async.AwaitTask
            ()
        with ex ->
            printfn "Erro ao notificar gateway: %s" ex.Message
    }



let verifyToken (token: string option) : Result<unit, WebPart> =
    match token with
    | Some t when t = SECRET_TOKEN -> Result.Ok ()
    | _ -> Result.Error (jsonResponse HTTP_403 {| status = "cancelled"; reason = "invalid token" |})

let parseBody (body: string) : Result<Map<string, obj>, WebPart> =
    try Result.Ok (JsonConvert.DeserializeObject<Map<string, obj>>(body))
    with _ -> Result.Error (jsonResponse HTTP_400 {| status = "cancelled"; reason = "invalid payload" |})

let validateTransactionId (data: Map<string, obj>) : Result<string, WebPart> =
    if data.ContainsKey("transaction_id") then
        Result.Ok (string data["transaction_id"])
    else
        Result.Error (jsonResponse HTTP_400 {| status = "cancelled"; reason = "missing field: transaction_id" |})

let validateRequiredFields (txId: string) (data: Map<string, obj>) : Result<unit, WebPart> =
    let required = [ "event"; "amount"; "currency"; "timestamp" ]
    let missing  = required |> List.tryFind (fun k -> not (data.ContainsKey(k)))
    match missing with
    | None       -> Result.Ok ()
    | Some field -> Result.Error (jsonResponse HTTP_400 {| status = "cancelled"; reason = sprintf "missing field: %s" field |})

let validateNotDuplicate (txId: string) : Result<unit, WebPart> =
    if isAlreadyProcessed txId then
        Result.Error (jsonResponse HTTP_400 {| status = "cancelled"; transaction_id = txId; reason = "transaction duplicated" |})
    else
        Result.Ok ()

let validateOrder (txId: string) (data: Map<string, obj>) : Result<unit, WebPart> =
    let amount   = sprintf "%.2f" (float (string data["amount"]))
    let currency = string data["currency"]
    if amount = "49.90" && currency = "BRL" then
        Result.Ok ()
    else
        Result.Error (jsonResponse HTTP_400 {| status = "cancelled"; transaction_id = txId; reason = "mismatch" |})



let tryHeader (name: string) (ctx: HttpContext) =
    match ctx.request.header name with
    | Choice1Of2 value -> Some value
    | Choice2Of2 _ -> None

let webhookHandler (ctx: HttpContext) = async {
    let token = tryHeader "X-Webhook-Token" ctx
    let body  = Text.Encoding.UTF8.GetString(ctx.request.rawForm)

    let authAndParse =
        verifyToken token
        |> Microsoft.FSharp.Core.Result.bind (fun _ -> parseBody body)
        |> Microsoft.FSharp.Core.Result.bind validateTransactionId

    match authAndParse with
    | Result.Error response -> return! response ctx
    | Result.Ok txId ->
        let data = parseBody body |> Microsoft.FSharp.Core.Result.defaultValue Map.empty

        let validateTransaction =
            validateRequiredFields txId data
            |> Microsoft.FSharp.Core.Result.bind (fun _ -> validateNotDuplicate txId)
            |> Microsoft.FSharp.Core.Result.bind (fun _ -> validateOrder txId data)

        match validateTransaction with
        | Result.Error response ->
            do! postToGateway cancelUrl txId
            return! response ctx
        | Result.Ok _ ->
            do! postToGateway confirmUrl txId
            return! jsonResponse HTTP_200 {| status = "confirmed"; transaction_id = txId |} ctx
}



[<EntryPoint>]
let main _ =
    let app =
        choose [
            POST >=> path "/webhook" >=> webhookHandler
            NOT_FOUND "rota não encontrada"
        ]
    let config = { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 5000 ] }
    startWebServer config app
    0