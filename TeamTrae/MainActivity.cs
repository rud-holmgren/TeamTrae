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

            var lmgr = (LocationManager)ApplicationContext.GetSystemService(Context.LocationService);
            lmgr.RequestLocationUpdates(LocationManager.GpsProvider, 0, 0, new MyLocationListener(this));

            _camhandler = new MyCameraHandler(this);

            //            _timer = new Timer(10000);
            //            _timer.AutoReset = true;
            //            _timer.Elapsed += HandleTimerTick;
            //            _timer.Start();

            _timerhandler = new Handler(this.MainLooper);
            _mytimer = new Runnable(() =>
            {
                if (this.IsDestroyed) { return; }

                _camhandler.TakePhoto();
                _timerhandler.PostDelayed(_mytimer, 10000);
            });
            _timerhandler.PostDelayed(_mytimer, 1000);
        }

        private Handler _timerhandler;
        private Runnable _mytimer;
        private MyCameraHandler _camhandler;
        private Location _lastKnownLocation;

        private void HandleTimerTick(object sender, EventArgs args)
        {
            _camhandler.TakePhoto();
        }

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

            public MyCameraHandler(MainActivity activity)
            {
                Activity = activity;

                _handlerThread = new HandlerThread("MyCameraHandler");
                _handlerThread.Start();
            }

            public ImageReader Target { get; private set; }
            public MainActivity Activity { get; }
            public int Rotation { get; set; }

            private class MyCaptureCallback : CameraCaptureSession.CaptureCallback
            {
                private readonly MyCameraHandler _owner;

                public MyCaptureCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
                {
                }

                public override void OnCaptureFailed(CameraCaptureSession session, CaptureRequest request, CaptureFailure failure)
                {
                    _owner._session = null;
                }
            }

            private CameraCaptureSession _session;
            private CaptureRequest _request;

            private CaptureRequest BuildCaptureRequest(CameraCaptureSession session)
            {
                var reqBuilder = session.Device.CreateCaptureRequest(CameraTemplate.StillCapture);
                reqBuilder.AddTarget(Target.Surface);

                // Focus
                reqBuilder.Set(CaptureRequest.ControlAfMode, new Java.Lang.Integer((int)ControlAFMode.ContinuousPicture));

                // GPS Location
                var location = Activity._lastKnownLocation;
                if (location != null)
                {
                    reqBuilder.Set(CaptureRequest.JpegGpsLocation, location);
                }

                var orientation = Activity.WindowManager.DefaultDisplay.Rotation;
                Rotation = (orientation == SurfaceOrientation.Rotation90) ? 0 : 180;
                reqBuilder.Set(CaptureRequest.JpegOrientation, new Java.Lang.Integer(Rotation));

                return reqBuilder.Build();
            }

            private void TakePhoto(CameraCaptureSession session, CaptureRequest request)
            {
                var cb = new MyCaptureCallback(this);
                session.Capture(request, cb, null);
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
                    _owner._session = session;
                    _owner._request = _owner.BuildCaptureRequest(session);
                    _owner.TakePhoto(_owner._session, _owner._request);
                    return;

                    var reqBuilder = session.Device.CreateCaptureRequest(CameraTemplate.StillCapture);
                    reqBuilder.AddTarget(_owner.Target.Surface);

                    // Focus
                    reqBuilder.Set(CaptureRequest.ControlAfMode, new Java.Lang.Integer((int)ControlAFMode.ContinuousPicture));

                    // GPS Location
                    var location = _owner.Activity._lastKnownLocation;
                    if (location != null)
                    {
                        reqBuilder.Set(CaptureRequest.JpegGpsLocation, location);
                    }

                    var orientation = _owner.Activity.WindowManager.DefaultDisplay.Rotation;
                    _owner.Rotation = (orientation == SurfaceOrientation.Rotation90) ? 0 : 180;
                    reqBuilder.Set(CaptureRequest.JpegOrientation, new Java.Lang.Integer(_owner.Rotation));

                    var cb = new MyCaptureCallback(_owner);
                    session.Capture(reqBuilder.Build(), cb, null);
                }

                public override void OnConfigureFailed(CameraCaptureSession session)
                {
                    _owner._session = null;
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
                    Image image = reader.AcquireNextImage();

                    ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                    byte[] bytes = new byte[buffer.Capacity()];
                    buffer.Get(bytes);

                    SendPhotoToServer(bytes);

                    Bitmap bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                    var imgview = _owner.Activity.FindViewById<ImageView>(Resource.Id.theimage);

                    _owner.Activity.RunOnUiThread(() =>
                    {
                        imgview.SetImageBitmap(bitmap);
                        imgview.Rotation = _owner.Rotation; //  == 90 ? 0 : 180;
                    });

                    image.Close();
                }

                private bool SendPhotoToServer(byte[] data)
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
                        return true;
                    }
                    catch (System.Exception e)
                    {
                        return false;
                    }
                }
            }

            private class MyStateCallback : CameraDevice.StateCallback
            {
                private readonly MyCameraHandler _owner;

                public MyStateCallback(MyCameraHandler owner)
                {
                    _owner = owner;
                }

                public override void OnDisconnected(CameraDevice camera)
                {
                    _owner._session = null;
                }

                public override void OnError(CameraDevice camera, [GeneratedEnum] Android.Hardware.Camera2.CameraError error)
                {
                    _owner._session = null;
                }

                public override void OnOpened(CameraDevice camera)
                {
                    try
                    {
                        // var req = camera.CreateCaptureRequest(CameraTemplate.StillCapture);
                        _owner.Target = ImageReader.NewInstance(1280, 720, Android.Graphics.ImageFormatType.Jpeg, 2);
                        _owner.Target.SetOnImageAvailableListener(new MyImageAvailableCallback(_owner), null);
                        var outputSurfaces = new List<Surface>()
                    {
                       _owner.Target.Surface
                    };

                        camera.CreateCaptureSession(outputSurfaces, new MySessionStateCallback(_owner), null);
                    }
                    catch
                    {
                        _owner._session = null;
                    }
                }
            }

            public void TakePhoto()
            {
                if (_session == null)
                {
                    var cmgr = (CameraManager)Activity.ApplicationContext.GetSystemService(Context.CameraService);
                    var ids = cmgr.GetCameraIdList();
                    cmgr.OpenCamera(ids[0], new MyStateCallback(this), new Handler(_handlerThread.Looper));
                }
                else
                {
                    new Handler(_handlerThread.Looper).Post(() => TakePhoto(this._session, this._request));
                }
            }
        }

        private class MyLocationListener : Java.Lang.Object, ILocationListener
        {
            public MyLocationListener(MainActivity activity)
            {
                Activity = activity;
            }

            public MainActivity Activity { get; }

            public void OnLocationChanged(Location location)
            {
                Activity._lastKnownLocation = location;
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

