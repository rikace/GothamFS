open System
open Util
open Computation
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Threading
open System.Windows.Threading
open System.Windows.Media.Imaging
open Microsoft.FSharp.NativeInterop

let getPixels(img:BitmapSource)  =    
    let stride = img.PixelWidth * 4
    let size = img.PixelHeight * stride
    let pixels = Array.zeroCreate<int> size
    img.CopyPixels(pixels, stride, 0)    
    pixels    

type ZoomControl(width:int,height:int) as self =
    inherit Window()
    
    let syncContext = System.Threading.SynchronizationContext()  
    let bitmap = WriteableBitmap(width,height, 96., 96., PixelFormats.Bgr32, null)
    let canvas = Canvas()
        
    do  
        self.Width <- float width
        self.Height <- float height
        canvas.Children.Add(Image(Source=bitmap)) |> ignore
        self.Content <- canvas 

    let render (x1,y1,x2,y2) (offset, height, stripe) (pixels:int array)  =   
        let dx, dy = x2-x1, y2-y1
        for y = 0 to height-1 do
            for x = 0 to width-1 do
                let y = offset + (y*stripe)
                pixels.[x+y*width] <-
                    let x = ((float x/float width) * dx) + x1
                    let y = ((float y/float (height*stripe)) * dy) + y1 
                    match Complex.Create(x, y) with
                    | DidNotEscape -> 0xff000000
                    | Escaped i -> 
                        let i = 255 - i
                        0xff000000 + (i <<< 16) +  (i <<< 8) + i
        ignore <| self.Dispatcher.Invoke(Action(fun () -> 
            bitmap.WritePixels(new Int32Rect(0, 0, width,(height * (offset + 1))), pixels, width * 4, offset)))

    let mutable points = (-2.0, -1.0, 1.0, 1.0)
    do render points (0,height,1) (getPixels(bitmap))
            
    let copy (l,t,w,h) =
        let selection =  WriteableBitmap(int w, int h, 96., 96., PixelFormats.Bgr32, null)
        let source = getPixels bitmap 
        for y = 0 to int h - 1 do
            for x = 0 to int w - 1 do
                let c = source.[int l + x + ((int t + y) * width)]
                selection.SetPixeli(x + (y * int w), c)

        selection

    let moveControl (element:FrameworkElement) (start:Point) (finish:Point) =
        element.Width <- abs(finish.X - start.X)
        element.Height <- abs(finish.Y - start.Y)
        Canvas.SetLeft(element, min start.X finish.X)
        Canvas.SetTop(element, min start.Y finish.Y)

    let transparentGray = 
        SolidColorBrush(Color.FromArgb(128uy, 164uy, 164uy, 164uy))
   
    let rec waiting() = async {
        let! md = Async.AwaitObservable(self.MouseLeftButtonDown)
        let rc = new Canvas(Background = transparentGray)
        canvas.Children.Add(rc) 
        do! drawing(rc, md.GetPosition(canvas)) }

    and drawing(rc:Canvas, pos) = async {
        let! evt = Async.AwaitObservable(canvas.MouseLeftButtonUp, canvas.MouseMove)
        match evt with
        | Choice1Of2(up) ->
            let l, t = Canvas.GetLeft(rc), Canvas.GetTop(rc)
            let w, h = rc.Width, rc.Height
            if w > 1.0 && h > 1.0 then
                let preview = 
                    Image(Source=copy (l,t,w,h),
                          Stretch=Stretch.Fill,
                          Width=float width,Height=float height)
                canvas.Children.Add preview

                let zoom (x1,y1,x2,y2) =           
                    let tx x = ((x/float width) * (x2-x1)) + x1
                    let ty y = ((y/float height) * (y2-y1)) + y1
                    tx l, ty t, tx (l+w), ty (t+h)

                points <- zoom points
              
                let threads = Environment.ProcessorCount
              
                let pixels = (getPixels(bitmap))
                do! [0..threads - 1] 
                        |> List.map (fun y ->                         
                                async { render points (y,(height/threads),threads) pixels
                            })
                        |> Async.Parallel 
                        |> Async.Ignore

                canvas.Children.Remove preview |> ignore

            canvas.Children.Remove rc |> ignore
            do! waiting() 
        | Choice2Of2(move) ->
            moveControl rc pos (move.GetPosition(canvas))
            do! drawing(rc, pos) }
    
    do  waiting() |> Async.StartImmediate

[<STAThread>]
(new Application()).Run(ZoomControl(512,484)) |> ignore  
    