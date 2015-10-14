module Agents 

    open System
    open System.Collections.Generic
    open System.Collections.ObjectModel
    open System.Windows
    open System.Windows.Data
    open System.Windows.Input
    open System.ComponentModel
    open OxyPlot
    open OxyPlot.Axes
    open OxyPlot.Series
    open System.Reactive.Linq
    open Messages


    type Agent<'a> = MailboxProcessor<'a>

    type private ThreadSafeRandomRequest =
    | GetDouble of AsyncReplyChannel<decimal>
    let private threadSafeRandomAgent = Agent.Start(fun inbox -> 
            let rnd = new Random()
            let rec loop() = async {
                let! GetDouble(reply) = inbox.Receive() 
                reply.Reply((rnd.Next(-5, 5) |> decimal))
                return! loop()
            }
            loop() )


    let updatePrice (price:decimal) =
                let newPrice' = price + (threadSafeRandomAgent.PostAndReply(GetDouble))
                if newPrice' < 0m then 5m
                elif newPrice' > 50m then 45m
                else newPrice'


    let stocksObservable (agent: MailboxProcessor<StockAgentMessage>) =
        Observable.Interval(TimeSpan.FromMilliseconds 750.)
        |> Observable.scan(fun s i -> updatePrice s) 20m
        |> Observable.add(fun u -> agent.Post(UpdateStockPrices(u, DateTime.Now)))

    let stockAgent (stockSymbol:string) =
        MailboxProcessor<StockAgentMessage>.Start(fun inbox -> 
            let rec loop stockPrice (subscriber:MailboxProcessor<ChartSeriesMessage> option) = async {
                let! msg = inbox.Receive()
                match msg with
                | UpdateStockPrices(p, d) ->  
                    match subscriber with
                    | None -> return! loop stockPrice subscriber
                    | Some(a) -> 
                        let message = HandleStockPrice(stockSymbol, p, d)
                        a.Post(message)  
                        return! loop stockPrice  subscriber
                | SubscribeStockPrices(s, a) -> 
                    match subscriber with
                    | None -> return! loop stockPrice (Some(a))
                    | _ -> return! loop stockPrice subscriber
                | UnSubscribeStockPrices(s) -> 
                    match subscriber with
                    | None -> return! loop stockPrice subscriber
                    | Some(a) -> 
                            (a :> IDisposable).Dispose()
                            return! loop stockPrice None }
            loop 20m None)

    let stocksCoordinatorAgent(lineChartingAgent:MailboxProcessor<ChartSeriesMessage>) =
        let stockAgents = Dictionary<string, MailboxProcessor<StockAgentMessage>>()
        MailboxProcessor<StocksCoordinatorMessage>.Start(fun inbox ->
                let rec loop() = async {
                    let! msg = inbox.Receive()
                    match msg with
                    | WatchStock(s) -> 
                        if not <| stockAgents.ContainsKey(s) then 
                            let stockAgentChild = stockAgent(s)
                            stockAgents.Add(s, stockAgentChild)
                            stockAgents.[s].Post(SubscribeStockPrices(s, lineChartingAgent))
                            lineChartingAgent.Post(AddSeriesToChart(s))          
                            stocksObservable(stockAgentChild)
                        return! loop()                                         
                    | UnWatchStock(s) ->
                        if stockAgents.ContainsKey(s) then
                             lineChartingAgent.Post(RemoveSeriesFromChart(s))  
                             stockAgents.[s].Post(UnSubscribeStockPrices(s))
                             (stockAgents.[s] :> IDisposable).Dispose()
                             stockAgents.Remove(s) |> ignore
                        return! loop() } 
                loop() )


    let lineChartingAgent(chartModel:PlotModel) =
        let refreshChart() = chartModel.InvalidatePlot(true)
        MailboxProcessor<ChartSeriesMessage>.Start(fun inbox ->
                let series =  Dictionary<string, LineSeries>()
                let rec loop() = async {
                    let! msg = inbox.Receive()
                    match msg with
                    | AddSeriesToChart(s) -> 
                            if not <| series.ContainsKey(s) then
                                let lineSeries = LineSeries()
                                lineSeries.StrokeThickness <- 2.
                                lineSeries.MarkerSize <- 3.
                                lineSeries.MarkerStroke <- OxyColors.Black
                                lineSeries.MarkerType <- MarkerType.None
                                lineSeries.CanTrackerInterpolatePoints <- false
                                lineSeries.Title <- s
                                lineSeries.Smooth <- false
                                series.Add (s, lineSeries)
                                chartModel.Series.Add lineSeries
                                refreshChart()
                                return! loop()
                    | RemoveSeriesFromChart(s) -> 
                            if series.ContainsKey(s) then
                                let seriesToRemove = series.[s]
                                chartModel.Series.Remove(seriesToRemove) |> ignore
                                series.Remove(s) |> ignore
                                refreshChart()
                            return! loop()
                    | HandleStockPrice(s,p, d) -> 
                            if series.ContainsKey(s) then
                                let newDataPoint = new DataPoint(DateTimeAxis.ToDouble(d), LinearAxis.ToDouble(p))
                                let seriesToUpdate = series.[s]
                                if seriesToUpdate.Points.Count > 10 then
                                    seriesToUpdate.Points.RemoveAt(0)
                                seriesToUpdate.Points.Add(newDataPoint)
                                series.[s] <- seriesToUpdate
                                refreshChart()                            
                            return! loop() }
                loop() )