#r "../../packages/Rx-Core.2.2.5/lib/net45/System.Reactive.Core.dll"
#r "../../packages/Rx-Interfaces.2.2.5/lib/net45/System.Reactive.Interfaces.dll"
#r "../../packages/Rx-Linq.2.2.5/lib/net45/System.Reactive.Linq.dll"
#r "../../packages/Rx-Xaml.2.2.5/lib/net45/System.Reactive.Windows.Threading.dll"
#r "WindowsBase.dll"
#r "PresentationCore.dll"
#r "PresentationFramework.dll"
#r "System.Xaml.dll"
#r "UIAutomationTypes.dll"
#load "Utils.fs"

open System
open System.Reactive
open System.Reactive.Linq
open System.Reactive.Subjects
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open AgentModule

module ``Observable Sensor`` =

    type SensorInfo = {TimeStamp:DateTime; SensorType:string; SensorValue:float}
        with override x.ToString() = 
                sprintf "Time: %s   -   Type: %s  Value: %f" 
                        (x.TimeStamp.ToShortTimeString()) x.SensorType x.SensorValue

                        
    type ObservableSensor() =

        let startSensor() =
            let randomizer = Random(DateTime.Now.Millisecond)
            let source = 
                Observable.Interval(TimeSpan.FromMilliseconds(randomizer.NextDouble() * 100.))
                |> Observable.map(fun _ -> {   SensorType = Math.Ceiling(randomizer.NextDouble() * 4.).ToString()
                                               SensorValue = (randomizer.NextDouble() * 20.)
                                               TimeStamp = DateTime.Now})
            source
    
        interface IObservable<SensorInfo> with            
            member x.Subscribe(observer:IObserver<SensorInfo>) =
                let sensor = startSensor()
                sensor.Subscribe(observer)


    


    let obsSensor = ObservableSensor()
    let disp = obsSensor.Subscribe(fun x -> printfn "Sensor Type %s\t - SensorValue %f" x.SensorType x.SensorValue)
    
    let disp' =
            obsSensor.Where(Func<SensorInfo, bool>(fun x -> x.SensorType = "2"))
             .Buffer(TimeSpan.FromSeconds(3.))
             .Subscribe(fun x -> printfn "2's per minute %A" (x.Count  * 20))


    disp.Dispose()
    disp'.Dispose()


module ``RX Stocks with Subject`` = 
    


    let msft = Stock.CreateStock("MSFT") 95.
    let amzn = Stock.CreateStock("AMZ") 197.
    let goog = Stock.CreateStock("GOOG") 513.

    let seqStocks = [msft;amzn;goog]  



    let sb = new Subject<Stock>()


    let updatedStocks (stocks:Stock list) =
            stocks |> List.map(fun s -> s.Update())

    let obs = { new IObserver<Stock> with
                    member x.OnNext(s) = printfn "Stock %s - price %4f" s.Symbol s.Price
                    member x.OnCompleted() = printfn "Completed"
                    member x.OnError(exn) = ()   }

    let dispose = sb.Subscribe(obs)   
    
    
    let stocksObservable =
        Observable.Interval(TimeSpan.FromMilliseconds (getThreadSafeRandom() * 100.))
        |> Observable.scan(fun s i -> updatedStocks s) seqStocks
        |> Observable.subscribe(fun s -> s |> List.iter (sb.OnNext))

    sb.OnCompleted()

    stocksObservable.Dispose()
    
    


module ``Follow the mouse with RX`` =

    open System.Windows 
    open System.Windows.Controls 
    open System.Windows.Media 

    type MyWindow() as this = 
        inherit Window()   

        let WIDTH = 20.0
        let canvas = new Canvas(Width=800.0, Height=400.0, Background = Brushes.White) 
        let chars = 
            "Reactive Extensions are awsome!"
            |> Seq.map (fun c -> 
                new TextBlock(Width=WIDTH, Height=30.0, FontSize=20.0, Text=string c, 
                              Foreground=Brushes.Black, Background=Brushes.White))
            |> Seq.toArray 
        do
            let title = sprintf "Mouse Move Sample - x = %d"
            this.Content <- canvas 
            this.Topmost <- true
            this.Title <- title 0
            this.SizeToContent <- SizeToContent.WidthAndHeight  
            for tb in chars do                                     
                canvas.Children.Add(tb) |> ignore 

            this.MouseMove 
            |> Observable.map (fun ea -> ea.GetPosition(this))
            |> Observable.filter (fun p -> p.X < 300.0)
            |> Observable.add (fun p -> 
                async {
                    this.Title <- title (int p.X)
                    for i in 0..chars.Length-1 do
                        do! Async.Sleep(90)
                        Canvas.SetTop(chars.[i], p.Y)
                        Canvas.SetLeft(chars.[i], p.X + float i*WIDTH)
                } |> Async.StartImmediate 
                (*  Async.StartImmediate to start an asynchronous 
                    computation on the current thread. Often, 
                    an asynchronous operation needs to update UI, 
                    which should always be done on the UI thread. 
                    When your asynchronous operation needs to begin 
                    by updating UI, Async.StartImmediate is a better choice *)
            ) 

    [<System.STAThread()>] 
    do  
        let app =  new Application() 
        app.Run(new MyWindow()) |> ignore 


module ``Reactive Animation`` =

    open System
    open System.Windows 
    open System.Windows.Controls 
    open System.Windows.Media 
    open System.Reactive
    open System.Reactive.Concurrency
    open System.Reactive.Linq
    open System.Windows.Shapes

    type MyWindow() as this = 
        inherit Window()   

        let WIDTH = 20.0
        let canvas = new Canvas(Width=800.0, Height=400.0, Background = Brushes.White) 
        let ellipse = Ellipse(Height=48., Width=48.)
        do
            let gr1 = GradientStop(Color=Colors.Azure, Offset=0.)
            let gr2 = GradientStop(Color=Colors.Blue, Offset=1.)
            let gsc = GradientStopCollection(seq{yield gr1; yield gr2})
            let rg = RadialGradientBrush(gsc)
            rg.GradientOrigin <- Point(0.3, 0.3)
            ellipse.Fill <- rg
            canvas.Children.Add(ellipse)

            this.Content <- canvas 
            this.Topmost <- true
            this.Title <- "Reactive Animation"
            this.SizeToContent <- SizeToContent.WidthAndHeight  


            let getAngleStreamByInterval(interval) : IObservable<float> =
                Observable.Interval(interval, ThreadPoolScheduler.Instance)
                |> Observable.map(fun i -> float(i) % 360.)
            
            
            let getAngleStream() : IObservable<float>=
                Observable.Generate(0., (fun _ -> true), 
                                        (fun i -> (i + 1.) % 360.), 
                                        (fun i -> i), 
                                        (fun _ -> TimeSpan.FromMilliseconds(10.)), 
                                        ThreadPoolScheduler.Instance)
            
            let locations = 
                getAngleStream()
                |> Observable.map(fun a ->  new Point(70. + a * 1.2, 200. + 150. * Math.Sin(Math.PI * a / 180.)))

            let setPosition(position:Point) =
                Canvas.SetLeft(ellipse, position.X)
                Canvas.SetTop(ellipse, position.Y)

            locations.SubscribeOn(DispatcherScheduler.Current).Subscribe(setPosition) |> ignore



    [<System.STAThread()>] 
    do  
        let app =  new Application() 
        app.Run(new MyWindow()) |> ignore 
