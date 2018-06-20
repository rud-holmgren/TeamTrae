using System;
using Android.App;
using Android.Widget;
using Android.OS;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Hardware;
using Android.Util;
using Java.Interop;
using Android;
using Android.Content.PM;
using Android.Runtime;
using Android.Hardware.Camera2;
using Android.Content;
using Android.Media;
using System.Collections.Generic;
using Android.Locations;
using System.Text.RegularExpressions;
using Android.Graphics;
using Java.Nio;
using Java.Net;
using Java.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Net;
using System.IO;
using Java.Lang;
using System.Linq;

namespace TeamTrae
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, LaunchMode = LaunchMode.SingleInstance)]
    public class MainActivity : AppCompatActivity
    {

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClick;


            _camhandler = new MyCameraHandler(this);

            _timerhandler = new Handler(this.MainLooper);
            _mytimer = new Runnable(() =>
            {
                if (this.IsDestroyed) { return; }

                try
                {
                    _camhandler.TakePhoto();
                    _timerhandler.PostDelayed(_mytimer, 5000);
                }
                catch (System.Exception e)
                {
                    _camhandler.OnError("MainLoop: " + e.Message);
                }
            });
            _timerhandler.PostDelayed(_mytimer, 1000);
        }

        private Handler _timerhandler;
        private Runnable _mytimer;
        private MyCameraHandler _camhandler;

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }



        private class MyCameraHandler
        {
            private HandlerThread _handlerThread;
            private CaptureWrapper _capture;
            private bool _lastCaptureComplete;
            private MyLocationListener _locationProvider;
            private LocationWrapper _lastKnownLocation;
            private PhotoUploader _uploader;

            public MyCameraHandler(MainActivity activity)
            {
                Activity = activity;

                _lastKnownLocation = new LocationWrapper(null);
                _locationProvider = new MyLocationListener(activity);
                _locationProvider.OnLocationUpdated += HandleLocationUpdated;

                _uploader = new PhotoUploader();
                _uploader.OnStateChanged += SetNetworkStatus;

                _handlerThread = new HandlerThread("MyCameraHandler");
                _handlerThread.Start();
            }

            public ImageReader Target { get; private set; }
            public MainActivity Activity { get; }
            public int Rotation { get; set; }

            public void TakePhoto()
            {
                var reuseCaptureSession = _lastCaptureComplete;

                _lastCaptureComplete = false;

                if (reuseCaptureSession)
                {
                    new Handler(_handlerThread.Looper).Post(CapturePhoto);
                }
                else
                {
                    var cmgr = (CameraManager)Activity.ApplicationContext.GetSystemService(Context.CameraService);
                    var ids = cmgr.GetCameraIdList();
                    cmgr.OpenCamera(ids[0], new MyCameraDeviceStateCallback(this), new Handler(_handlerThread.Looper));
                }
            }

            private void CapturePhoto()
            {
                var orientation = Activity.WindowManager.DefaultDisplay.Rotation;
                this.Rotation = (orientation == SurfaceOrientation.Rotation90) ? 0 : 180;

                _capture.TakePhoto(Target, _lastKnownLocation?.Location, Rotation);
            }

            private void SetNetworkStatus(string message)
            {
                SetUIText(Resource.Id.netstatus, "N", message);
            }

            private void OnCaptureComplete()
            {
                _lastCaptureComplete = true;
                SetUIText(Resource.Id.status, "S", "Capture complete");

                if (_lastKnownLocation != null && _lastKnownLocation.Timestamp.AddSeconds(30) < DateTime.Now)
                {
                    SetLocationText("Out-of-date");
                    _locationProvider.Reset();
                }
            }

            public void OnError(string message)
            {
                SetUIText(Resource.Id.status, "S", message);
            }

            private void OnNetworkError(string message)
            {
                SetUIText(Resource.Id.netstatus, "N", message);
            }

            private void SetLocationText(string message)
            {
                SetUIText(Resource.Id.locstatus, "L", message);
            }

            private void SetUIText(int id, string prefix, string msg)
            {
                var statustext = Activity.FindViewById<TextView>(id);

                Activity.RunOnUiThread(() =>
                {
                    var time = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                    statustext.Text = $"{prefix}:{time}:{msg}";
                });
            }

            private void OnCameraDeviceOpened(CameraDevice camera)
            {
                try
                {
                    Target = ImageReader.NewInstance(1280, 720, Android.Graphics.ImageFormatType.Jpeg, 2);
                    Target.SetOnImageAvailableListener(new MyImageAvailableCallback(this), null);
                    var outputSurfaces = new List<Surface>()
                    {
                       Target.Surface
                    };

                    camera.CreateCaptureSession(outputSurfaces, new MySessionStateCallback(this), null);
                }
                catch (System.Exception sex)
                {
                    OnError("Failed to create session: " + sex.Message);
                }
            }

            private void OnSessionConfigured(CameraCaptureSession session)
            {
                try
                {
                    _capture = new CaptureWrapper(session, new MyCaptureCallback(this));
                    CapturePhoto();
                }
                catch (System.Exception e)
                {
                    OnError("Failed to wrap session: " + e.Message);
                }
            }

            private void OnImageAvailable(ImageReader reader)
            {
                try
                {
                    Image image = reader.AcquireNextImage();

                    ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                    byte[] bytes = new byte[buffer.Capacity()];
                    buffer.Get(bytes);

                    _uploader.Queue(bytes);

                    Bitmap bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                    var imgview = Activity.FindViewById<ImageView>(Resource.Id.theimage);

                    Activity.RunOnUiThread(() =>
                    {
                        imgview.SetImageBitmap(bitmap);
                        imgview.Rotation = Rotation; //  == 90 ? 0 : 180;
                    });

                    image.Close();
                }
                catch (System.Exception e)
                {
                    OnError("Failed to process image:" + e.Message);
                }
            }


            private void HandleLocationUpdated(Location loc)
            {
                _lastKnownLocation = new LocationWrapper(loc);
                SetLocationText(loc.ToString());
            }

            private class CaptureWrapper
            {
                private readonly CameraCaptureSession _session;
                private readonly CameraCaptureSession.CaptureCallback _captureCallback;

                public CaptureWrapper(CameraCaptureSession session, CameraCaptureSession.CaptureCallback captureCallback)
                {
                    _session = session;
                    _captureCallback = captureCallback;
                }

                private CaptureRequest BuildCaptureRequest(ImageReader target, Location location, int rotation)
                {
                    var reqBuilder = _session.Device.CreateCaptureRequest(CameraTemplate.StillCapture);
                    reqBuilder.AddTarget(target.Surface);

                    // Focus
                    reqBuilder.Set(CaptureRequest.ControlAfMode, new Java.Lang.Integer((int)ControlAFMode.ContinuousPicture));

                    // GPS Location
                    if (location != null)
                    {
                        reqBuilder.Set(CaptureRequest.JpegGpsLocation, location);
                    }

                    reqBuilder.Set(CaptureRequest.JpegOrientation, new Java.Lang.Integer(rotation));

                    return reqBuilder.Build();
                }

                public void TakePhoto(ImageReader target, Location location, int rotation)
                {
                    var req = BuildCaptureRequest(target, location, rotation);
                    _session.Capture(req, _captureCallback, null);
                }
            }

            private class LocationWrapper
            {
                public LocationWrapper(Location loc)
                {
                    Location = loc;
                }

                public DateTime Timestamp { get; } = DateTime.Now;

                public Location Location { get; }
            }


            private class MyCameraDeviceStateCallback : CameraDevice.StateCallback
            {
                private readonly MyCameraHandler _owner;

                public MyCameraDeviceStateCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public override void OnDisconnected(CameraDevice camera)
                {
                    _owner.OnError("Camera Device disconnected");
                }

                public override void OnError(CameraDevice camera, [GeneratedEnum] Android.Hardware.Camera2.CameraError error)
                {
                    _owner.OnError("Camera Device error: " + error.ToString());
                }

                public override void OnOpened(CameraDevice camera)
                {
                    _owner.OnCameraDeviceOpened(camera);
                }
            }


            private class MyCaptureCallback : CameraCaptureSession.CaptureCallback
            {
                private readonly MyCameraHandler _owner;

                public MyCaptureCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
                {
                    _owner.OnCaptureComplete();
                }

                public override void OnCaptureFailed(CameraCaptureSession session, CaptureRequest request, CaptureFailure failure)
                {
                    _owner.OnError("Capture Failed: " + failure.ToString());
                }
            }


            private class MySessionStateCallback : CameraCaptureSession.StateCallback
            {
                private readonly MyCameraHandler _owner;

                public MySessionStateCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public override void OnConfigured(CameraCaptureSession session)
                {
                    _owner.OnSessionConfigured(session);
                }

                public override void OnConfigureFailed(CameraCaptureSession session)
                {
                    _owner.OnError("Session Configuration failed");
                }
            }

            private class MyImageAvailableCallback : Java.Lang.Object, ImageReader.IOnImageAvailableListener
            {
                private readonly MyCameraHandler _owner;

                public MyImageAvailableCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public void OnImageAvailable(ImageReader reader)
                {
                    _owner.OnImageAvailable(reader);
                }
            }


        }

        private class MyLocationListener : Java.Lang.Object, ILocationListener
        {
            private LocationManager _lmgr;

            public MyLocationListener(MainActivity activity)
            {
                Activity = activity;
                _lmgr = (LocationManager)activity.ApplicationContext.GetSystemService(Context.LocationService);
                Subscribe();
            }

            public void Reset()
            {
                UnSubscribe();
                Subscribe();
            }

            private void Subscribe()
            {
                _lmgr.RequestLocationUpdates(LocationManager.GpsProvider, 0, 0, this);
            }

            private void UnSubscribe()
            {
                _lmgr.RemoveUpdates(this);
            }

            public MainActivity Activity { get; }

            public event Action<Location> OnLocationUpdated;

            public void OnLocationChanged(Location location)
            {
                OnLocationUpdated?.Invoke(location);
            }

            public void OnProviderDisabled(string provider)
            {
            }

            public void OnProviderEnabled(string provider)
            {
            }

            public void OnStatusChanged(string provider, [GeneratedEnum] Availability status, Bundle extras)
            {
            }
        }


        class PhotoUploader
        {
            private HandlerThread _handlerThread;
            private Handler _uploadHandler;
            private Queue<PhotoWrapper> _queue = new Queue<PhotoWrapper>();

            private class PhotoWrapper
            {
                public PhotoWrapper(byte[] data)
                {
                    Data = data;
                }

                public byte[] Data { get; }
                public int Length => Data.Length;
            }

            public PhotoUploader()
            {                
                _handlerThread = new HandlerThread("MyPhotoUploader");
                _handlerThread.Start();

                _uploadHandler = new Handler(_handlerThread.Looper);
            }

            public event Action<string> OnStateChanged;

            private void UpdateState(string msg)
            {
                int nel, size;

                lock (_queue)
                {
                    nel = _queue.Count;
                    size = _queue.Sum(p => p.Length) / 1024;
                }

                msg += $" ({nel}/{size})";
                OnStateChanged?.Invoke(msg);
            }

            public void Queue(byte[] data)
            {
                lock (_queue)
                {
                    _queue.Enqueue(new PhotoWrapper(data));
                    TrimQueue();
                }

                _uploadHandler.Post(UploadQueue);
            }

            private void TrimQueue()
            {

            }

            private PhotoWrapper Dequeue()
            {
                lock (_queue)
                {
                    return (_queue.Count > 0) ? _queue.Dequeue() : null;
                }
            }

            private PhotoWrapper Peek()
            {
                lock (_queue)
                {
                    return (_queue.Count > 0) ? _queue.Peek() : null;
                }
            }

            private void UploadQueue()
            {
                PhotoWrapper p;
                string msg = null;
                bool success = true;

                while (success && (p = Peek()) != null)
                {
                    success = SendPhotoToServer(p.Data, out msg);

                    if (success)
                    {
                        Dequeue();
                    }

                    UpdateState(msg);
                }
            }

            private bool SendPhotoToServer(byte[] data, out string message)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://teamtrae.azurewebsites.net/api/Photo");

                    req.Method = "POST";

                    var b64 = Convert.ToBase64String(data);
                    var b64bin = System.Text.Encoding.ASCII.GetBytes(b64);

                    using (var sw = new StreamWriter(req.GetRequestStream()))
                    {
                        sw.Write(b64);
                    }

                    var resp = req.GetResponse();
                    message = "OK";
                    return true;
                }
                catch (System.Exception e)
                {
                    message = e.Message;
                    return false;
                }
            }
        }

        readonly string[] PermissionsCamera =
        {
            Manifest.Permission.Camera,
            Manifest.Permission.AccessFineLocation,
            Manifest.Permission.AccessCoarseLocation,
            Manifest.Permission.Internet
        };

        // const int RequestLocationId = 0;

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            const string permission = Manifest.Permission.Internet;
            if (CheckSelfPermission(permission) != (int)Permission.Granted)
            {
                RequestPermissions(PermissionsCamera, 0);
                return;
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}

