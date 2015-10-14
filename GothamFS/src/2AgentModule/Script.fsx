#load "Utils.fs"
open System.IO
open System
open System.Threading
open System.Collections.Generic
open System.Net
open AgentModule

module ``Agent 101`` = 

 
    let cancellationToken = new CancellationTokenSource()

    type internal MessageCounter = 
        | Increment of int 
        | Fetch of AsyncReplyChannel<int>
        | Operatiom of (int -> int) * AsyncReplyChannel<int>
        | Stop 
        | Pause
        | Resume

    type CountingAgent() =
        let counter = MailboxProcessor.Start((fun inbox ->
             let rec blocked(n) =           
                inbox.Scan(fun msg ->
                match msg with
                | Resume -> Some(async {
                    return! processing(n) })
                | _ -> None)

             and processing(n) = async {
                        let! msg = inbox.Receive()
                        match msg with
                        | Increment m -> return! processing(n + m)
                        | Operatiom(op, reply) -> 
                                let asyncOp = async {   let result = op(n)
                                                        do! Async.Sleep 5000
                                                        reply.Reply(result) }
                                Async.Start(asyncOp)
                                return! processing(n)
                        | Stop -> ()
                        | Resume -> return! processing(n)
                        | Pause -> return! blocked(n)
                        | Fetch replyChannel  ->    do replyChannel.Reply n
                                                    return! processing(n) }
             processing(0)), cancellationToken.Token)

        member a.Increment(n) = counter.Post(Increment n)       
        member a.Stop() =   cancellationToken.Cancel()
                            //counter.Post Stop
        member a.Pause() = counter.Post Pause
        member a.Resume() = counter.Post Resume

        member a.Fetch() = counter.PostAndReply(fun replyChannel -> Fetch replyChannel)
        member a.FetchAsync(continuation) = 
                let opAsync = counter.PostAndAsyncReply(fun replyChannel -> Fetch replyChannel)
                Async.StartWithContinuations(opAsync, 
                    (fun reply -> continuation reply), //continuation
                    (fun _ -> ()), //exception
                    (fun _ -> ())) //cancellation

        member a.Operation (f:(int -> int)) continuation =
                let opAsync = counter.PostAndAsyncReply(fun replyChannel -> Operatiom(f, replyChannel))
                Async.StartWithContinuations(opAsync, 
                    (fun reply -> continuation reply), //continuation
                    (fun _ -> ()), //exception
                    (fun _ -> ())) //cancellation

    let counterInc = new CountingAgent()

    counterInc.Increment(1)
    counterInc.Fetch()
    counterInc.Pause()
    counterInc.Increment(2)
    counterInc.Resume()
    counterInc.Fetch()

    counterInc.FetchAsync(fun res -> printfn "Reply Async received: %d" res)

    let add2 = (+) 2
    counterInc.Operation(add2) (fun res -> printfn "Reply 'add2' received: %d" res)
    counterInc.Fetch()

    let mult3 = (*) 3
    counterInc.Operation(add2) (fun res -> printfn "Reply 'mul3' received: %d" res)


    counterInc.Fetch()
    counterInc.Stop()


module ``Agent Parent-Children`` =

    type Message =
        | Register of id:int
        | Unregister of id:int
        | SendMeessage of id:int * message:string
        | BroadcastMeessage of message:string
        | ThrowError

    let cancellationToken = new CancellationTokenSource()
    
        
    let errorAgent =
           Agent<int * Exception>.Start((fun inbox ->
                 async {   while true do
                           let! id, error = inbox.Receive()
                           printfn "an error '%s' occurred in Agent %d" error.Message id}),
                cancellationToken.Token)

    
 
    let agentChild(id:int) =
        let agent = Agent<string>.Start((fun inbox ->
            let rec loop messages = async{
                let! msg = inbox.Receive()
                if msg = "throw error" then raise(new Exception(sprintf "Error from Agent %d" id))
                else printfn "Message received Agent id [%d] - %s" id msg
                return! loop (msg::messages) }
            loop []), cancellationToken.Token)
        agent.Error.Add(fun error -> errorAgent.Post (id,error))        
        agent



    let agentParent =
            Agent<Message>.Start((fun inbox ->
                let agents = new Dictionary<int, Agent<string>>(HashIdentity.Structural)    
                let cancellationToken = new CancellationTokenSource()            
                let rec loop count = async {
                    let! msg = inbox.Receive()
                    match msg with 
                    | Register(id) -> 
                            if not <| agents.ContainsKey id then
                                let newAgentChild = agentChild(id)
                                agents.Add(id, newAgentChild)
                            return! loop (count + 1) 
                    | Unregister(id) -> 
                            if agents.ContainsKey id then
                                let agentToRemove = agents.[id]
                                (agentToRemove :> IDisposable).Dispose()
                                agents.Remove(id) |> ignore
                            return! loop (count - 1)
                    | SendMeessage(id, message) -> 
                        if agents.ContainsKey id then
                            let agentToSendMessage = agents.[id]
                            agentToSendMessage.Post(message)
                        return! loop count
                    | BroadcastMeessage(message) ->
                        for KeyValue(id, agent) in agents do
                            agent.Post(message)
                        return! loop count 
                    | ThrowError -> 
                        agents
                        |> Seq.filter(fun (KeyValue(id, _)) -> id % 2 = 0)
                        |> Seq.iter(fun (KeyValue(id, agent)) -> 
                                inbox.Post(SendMeessage(id, "throw error")))
                        return! loop count }
                loop 0), cancellationToken.Token)

             
    for id in [0..100000] do
        agentParent.Post(Register(id))

    agentParent.Post(SendMeessage(4, "Hello!"))
    agentParent.Post(ThrowError)
    agentParent.Post(SendMeessage(4, "Hello!"))
    agentParent.Post(SendMeessage(7, "Hello!"))
    agentParent.Post(Unregister(7))
    agentParent.Post(SendMeessage(7, "Hello!"))
    agentParent.Post(SendMeessage(9, "Hello!"))
    cancellationToken.Cancel()
    agentParent.Post(SendMeessage(9, "Hello!"))

   
   module ``Agent message-sec`` =

    
    let agent =
        Agent<int>.Start(fun inbox ->
                let sw = System.Diagnostics.Stopwatch()
                let rec loop() = async{
                    let! msg = inbox.Receive()
                    if msg = 0 then 
                        sw.Start()
                        return! loop()
                    elif msg = 30000000 then
                        printfn "Last message arrived - %d ms - %d message per sec" sw.ElapsedMilliseconds (30000000/  sw.Elapsed.Seconds)
                    else
                        return! loop() }
                loop())

    let sw = System.Diagnostics.Stopwatch.StartNew()
    [0..30000000] 
    |> List.iter(agent.Post)
    printfn "Last message sent - %d ms - %d message per sec" sw.ElapsedMilliseconds (30000000/  sw.Elapsed.Seconds)


