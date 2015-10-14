module MainApp

open System
open System.Windows
open System.Windows.Controls

open FSharpx
type MainWindow = XAML<"MainWindow.xaml">

type DragState = { 
    /// Are we currently dragging?
    dragging : bool;
    /// Current position of the drag
    position : Point; 
    /// Drag offset, for taking into account initial drag position
    offset : Point }

/// A type of change that can happen to the DragState
type DragChange = 
    /// Start dragging from a certain offset
    | StartDrag of Point 
    /// Stop dragging
    | StopDrag 
    /// Update the drag with a new position
    | UpdatePosition of Point


/// Initialized the application
let loadWindow() =
    let window = MainWindow()

    /// StartDrag events
    let start_drag = 
        window.Ball.MouseDown
        |> Observable.filter (fun btn -> btn.ChangedButton = Input.MouseButton.Left)
        |> Observable.map (fun x -> StartDrag(x.GetPosition(window.Ball)))

    /// StopDrag events
    let stop_drag =
        window.Canvas.MouseUp
        |> Observable.filter(fun btn -> btn.ChangedButton = Input.MouseButton.Left)
        |> Observable.map (fun _ -> StopDrag)

    /// UpdatePosition events
    let moving =
        window.Canvas.MouseMove
        |> Observable.map (fun x -> UpdatePosition(x.GetPosition(window.Canvas)))


    /// Subscription for the entire Drag command
    let subscription =

    // OPTION (1) 
        // TODO:    Build functionality to Drag the Ball whee the mouse is pressed, and drwa 
            // Possible approach
            // TODO:    Merge the events "start_dragging", "stop_dragging", "moving" 
            // TODO:    Check the event fired and the current state (StartDragging, StopDrag and UpdatePosition)
            //          Modify the old state state with the current one
            // TODO:    Check and filter the state if it is dragging
            // TODO:    Retrive the current mouse coordinates
            // TODO:    Move the Ball accordingly

    // OPTION (2) 
        // TODO:    Add drawing functionality (may be a red line)
        // TODO:    Collect the coordinate Points during the dragging,
        //          Create an Undo logic (memento pattern), when the mouse is released
        //          the ball goes backward (adding some delay for animation)
        //          following the original path till the original starting point.

          List.reduce Observable.merge [start_drag 
                                        stop_drag
                                        moving ]

        |> Observable.scan (fun (state : DragState) (change : DragChange) -> 
                            match change with
                            | StartDrag(offset) -> { state with dragging=true; offset=offset }
                            | StopDrag -> { state with dragging=false}
                            | UpdatePosition(pos) ->  if state.dragging = true 
                                                            then { state with position=pos }
                                                      else state)
            { dragging=false; position=new Point(); offset=new Point() }
        |> Observable.filter (fun state -> state.dragging = true)        
        |> Observable.map (fun (state : DragState) ->
                    let diff = state.position - state.offset
                    Point(diff.X, diff.Y))
        |> Observable.subscribe (fun (position : Point) -> 
                    Canvas.SetLeft(window.Ball, position.X)
                    Canvas.SetTop(window.Ball, position.Y))

    window.Root

[<STAThread>]
(new Application()).Run(loadWindow()) |> ignore
