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
    open Utils


    let stocksObservable (agent: Agent<StockAgentMessage>) =
        Observable.Interval(TimeSpan.FromMilliseconds 750.)
        |> Observable.scan(fun s i -> updatePrice s) 20m
        |> Observable.add(fun u -> agent.Post({ Price=u; Time=DateTime.Now}))

    let stockAgentObs (stockSymbol:string, chartAgent:Agent<ChartSeriesMessage>) =
        Agent<StockAgentMessage>.Start(fun inbox -> 
            stocksObservable(inbox)
            let rec loop stockPrice (chartAgent:Agent<ChartSeriesMessage>) = async {
                let! { Price=price; Time=time } = inbox.Receive()
                let message = HandleStockPrice(stockSymbol, price, time)
                chartAgent.Post(message)  
                return! loop stockPrice chartAgent }
            loop 20m chartAgent)

    
    
    
    // TODO:    Create a stock-Agent using the MailboxProcessor     
    //          There will be one Agent for each stock-symbol
    //          This Agent is keeping track of the price of the Stock (at least the last one), 
    //          and it updates the chart-Agent by sending the update value
    // TODO:    For Updating the price you would need a timer, possible options are
    //          Use the Agent timer built in (TryReceive)   

    let stockAgent (stockSymbol:string, chartAgent:Agent<ChartSeriesMessage>) = ()



    // TODO:    Create a coordinator-Agent using the MailboxProcessor    
    //          This agent keeps track of the subsscribed stock-Agents, one per stock symbol
    //          Use internal state to add new stock agents when requested, 
    //          or for removing existing stock-Agents when the stock symbol is removed

    let stocksCoordinatorAgent(lineChartingAgent:Agent<ChartSeriesMessage>) = ()


    let lineChartingAgent(chartModel:PlotModel) =
        let refreshChart() = chartModel.InvalidatePlot(true)
        Agent<ChartSeriesMessage>.Start(fun inbox ->
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