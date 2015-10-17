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


// ===========================================
// Observable Sensor
// ===========================================
 

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
