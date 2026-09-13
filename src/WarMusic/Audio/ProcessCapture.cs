using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;
namespace WarMusic.Audio;

// Windows process loopback activates an IAudioClient for one process tree, never the endpoint mix.
public sealed class ProcessCapture : IDisposable
{
 readonly CancellationTokenSource stop=new();
 Task? worker;
 int disposed;
 public event Action<byte[],int>? Data;
 public event Action<Exception>? Failed;
 public static readonly WaveFormat Format=new(48000,16,2);
 public Task StartAsync(int processId)
 {
  var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  worker=Task.Factory.StartNew(()=>Run(processId,ready),TaskCreationOptions.LongRunning);
  return ready.Task;
 }
 void Run(int pid,TaskCompletionSource ready)
 {
  AudioClient? client=null;IActivateAudioInterfaceAsyncOperation? operation=null;
  IntPtr parameters=IntPtr.Zero,variant=IntPtr.Zero;
  try
  {
   parameters=Marshal.AllocHGlobal(12);Marshal.WriteInt32(parameters,0,1);Marshal.WriteInt32(parameters,4,pid);Marshal.WriteInt32(parameters,8,0);
   variant=Marshal.AllocHGlobal(24);for(int i=0;i<24;i++)Marshal.WriteByte(variant,i,0);
   Marshal.WriteInt16(variant,0,65);Marshal.WriteInt32(variant,8,12);Marshal.WriteIntPtr(variant,16,parameters);
   var completion=new Completion();
   Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback",typeof(IAudioClient).GUID,variant,completion,out operation));
   // Keep activation storage alive until the callback has consumed it.
   if(!completion.Result.Task.Wait(TimeSpan.FromSeconds(12)))
   {
    var retainedParameters=parameters;var retainedVariant=variant;var retainedOperation=operation;
    parameters=variant=IntPtr.Zero;operation=null;
    _=completion.Result.Task.ContinueWith(t=>{if(t.IsCompletedSuccessfully)Marshal.ReleaseComObject(t.Result);Marshal.FreeHGlobal(retainedParameters);Marshal.FreeHGlobal(retainedVariant);if(retainedOperation!=null)Marshal.ReleaseComObject(retainedOperation);GC.KeepAlive(completion);});
    throw new TimeoutException("Windows did not finish application capture activation. Reconnect the source to retry.");
   }
   client=new AudioClient((IAudioClient)completion.Result.Task.GetAwaiter().GetResult());
   using var sampleReady=new AutoResetEvent(false);
   client.Initialize(AudioClientShareMode.Shared,AudioClientStreamFlags.Loopback|AudioClientStreamFlags.EventCallback|AudioClientStreamFlags.AutoConvertPcm,0,0,Format,Guid.Empty);
   client.SetEventHandle(sampleReady.SafeWaitHandle.DangerousGetHandle());
   var capture=client.AudioCaptureClient;byte[] bytes=new byte[Format.AverageBytesPerSecond];
   client.Start();ready.TrySetResult();
   WaitHandle[] events=[stop.Token.WaitHandle,sampleReady];
   while(!stop.IsCancellationRequested)
   {
    if(WaitHandle.WaitAny(events,100)==0)break;
    while(capture.GetNextPacketSize()>0)
    {
     var pointer=capture.GetBuffer(out int frames,out var flags);
     try {int length=frames*Format.BlockAlign;if(length>bytes.Length)throw new InvalidOperationException("Unexpected application capture packet size.");if((flags&AudioClientBufferFlags.Silent)!=0)Array.Clear(bytes,0,length);else Marshal.Copy(pointer,bytes,0,length);Data?.Invoke(bytes,length);}
     finally{capture.ReleaseBuffer(frames);}
    }
   }
   client.Stop();GC.KeepAlive(completion);
  }
  catch(Exception ex){ready.TrySetException(ex);if(!stop.IsCancellationRequested)Failed?.Invoke(ex);}
  finally{client?.Dispose();if(operation!=null)Marshal.ReleaseComObject(operation);if(parameters!=IntPtr.Zero)Marshal.FreeHGlobal(parameters);if(variant!=IntPtr.Zero)Marshal.FreeHGlobal(variant);}
 }
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();if(worker!=null&&Task.CurrentId!=worker.Id)worker.GetAwaiter().GetResult();stop.Dispose();}
 [DllImport("Mmdevapi.dll",ExactSpelling=true,PreserveSig=true)]
 static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)]string path,[MarshalAs(UnmanagedType.LPStruct)]Guid iid,IntPtr activationParams,IActivateAudioInterfaceCompletionHandler handler,out IActivateAudioInterfaceAsyncOperation operation);
 [ComVisible(true),Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 public interface IAgileObject { }
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
 public sealed class Completion:IActivateAudioInterfaceCompletionHandler,IAgileObject
 {
  public TaskCompletionSource<object> Result {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation){try{operation.GetActivateResult(out int hr,out object value);Marshal.ThrowExceptionForHR(hr);Result.TrySetResult(value);}catch(Exception ex){Result.TrySetException(ex);}}
 }
}
