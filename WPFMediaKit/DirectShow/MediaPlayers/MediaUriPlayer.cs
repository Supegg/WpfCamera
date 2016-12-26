﻿#region Usings
using System;
using System.Runtime.InteropServices;
using DirectShowLib;
#endregion

namespace WPFMediaKit.DirectShow.MediaPlayers
{
    [ComImport, Guid("04FE9017-F873-410E-871E-AB91661A4EF7")]
    internal class FFDShow
    {

    }


    [ComImport, Guid("B98D13E7-55DB-4385-A33D-09FD1BA26338")]
    public class LAVSplitterSource { }


    [ComImport, Guid("171252A0-8820-4AFE-9DF8-5C92B2D66B04")]
    public class LAVSplitter { }



    /// <summary>
    /// The MediaUriPlayer plays media files from a given Uri.
    /// </summary>
    public class MediaUriPlayer : MediaSeekingPlayer
    {
        /// <summary>
        /// The name of the default audio render.  This is the
        /// same on all versions of windows
        /// </summary>
        private const string DEFAULT_AUDIO_RENDERER_NAME = "Default DirectSound Device";

        /// <summary>
        /// Set the default audio renderer property backing
        /// </summary>
        private string m_audioRenderer = DEFAULT_AUDIO_RENDERER_NAME;

#if DEBUG
        /// <summary>
        /// Used to view the graph in graphedit
        /// </summary>
        private DsROTEntry m_dsRotEntry;
#endif

        /// <summary>
        /// The DirectShow graph interface.  In this example
        /// We keep reference to this so we can dispose 
        /// of it later.
        /// </summary>
        private IGraphBuilder m_graph;

        public IGraphBuilder Graph
        {
            get { return m_graph; }
        }


        public MediaUriPlayer()
        {
            Splitter = "LAV Splitter";
            SplitterSource = "LAV Splitter Source";
            VideoDecoder = "LAV Video Decoder";
            AudioDecoder = "LAV Audio Decoder";
        }


        /// <summary>
        /// The media Uri
        /// </summary>
        private Uri m_sourceUri;

        /// <summary>
        /// Gets or sets the Uri source of the media
        /// </summary>
        public Uri Source
        {
            get
            {
                VerifyAccess();
                return m_sourceUri;
            }
            set
            {
                VerifyAccess();
                m_sourceUri = value;

                OpenSource();
            }
        }

        /// <summary>
        /// The renderer type to use when
        /// rendering video
        /// </summary>
        public VideoRendererType VideoRenderer
        {
            get;
            set;
        }

        /// <summary>
        /// The name of the audio renderer device
        /// </summary>
        public string AudioRenderer
        {
            get
            {
                VerifyAccess();
                return m_audioRenderer;
            }
            set
            {
                VerifyAccess();

                if (string.IsNullOrEmpty(value))
                {
                    value = DEFAULT_AUDIO_RENDERER_NAME;
                }

                m_audioRenderer = value;
            }
        }

        /// <summary>
        /// Gets or sets if the media should play in loop
        /// or if it should just stop when the media is complete
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Is ran everytime a new media event occurs on the graph
        /// </summary>
        /// <param name="code">The Event code that occured</param>
        /// <param name="lparam1">The first event parameter sent by the graph</param>
        /// <param name="lparam2">The second event parameter sent by the graph</param>
        protected override void OnMediaEvent(EventCode code, IntPtr lparam1, IntPtr lparam2)
        {
            if (Loop)
            {
                switch (code)
                {
                    case EventCode.Complete:
                        MediaPosition = 0;
                        break;
                }
            }
            else
                /* Only run the base when we don't loop
                 * otherwise the default behavior is to
                 * fire a media ended event */
                base.OnMediaEvent(code, lparam1, lparam2);
        }

        public void DumpGraphInfo(string fileName)
        {
            DumpGraphInfo(fileName, m_graph);
        }

        public void DumpGraphInfo(string fileName, IGraphBuilder graph)
        {
            if (string.IsNullOrEmpty(fileName) || graph == null) return;

            string filterOutputString = string.Empty;

            IEnumFilters enumFilters;
            int hr = graph.EnumFilters(out enumFilters);
            DsError.ThrowExceptionForHR(hr);

            IntPtr fetched = IntPtr.Zero;

            IBaseFilter[] filters = { null };


            int r = 0;
            while (r == 0)
            {
                try
                {
                    r = enumFilters.Next(filters.Length, filters, fetched);
                    DsError.ThrowExceptionForHR(r);

                    if (filters == null || filters.Length == 0 || filters[0] == null) continue;

                    FilterInfo filterInfo;
                    filters[0].QueryFilterInfo(out filterInfo);



                    filterOutputString += string.Format("{0:X8}", Marshal.GetIUnknownForObjectInContext(filters[0]).ToInt32()) + " ";

                    filterOutputString += filterInfo.achName + Environment.NewLine;


                    /* We will want to enum all the pins on the source filter */
                    IEnumPins pinEnum;

                    hr = filters[0].EnumPins(out pinEnum);
                    DsError.ThrowExceptionForHR(hr);

                    IntPtr fetched2 = IntPtr.Zero;
                    IPin[] pins = { null };

                    while (pinEnum.Next(pins.Length, pins, fetched2) == 0)
                    {
                        PinInfo pinInfo;
                        pins[0].QueryPinInfo(out pinInfo);

                        var prefix = "[In ] ";
                        if (pinInfo.dir == PinDirection.Output)
                            prefix = "[Out] ";


                        filterOutputString += string.Format("{0:X8}", Marshal.GetIUnknownForObjectInContext(pins[0]).ToInt32()) + " ";
                        filterOutputString += prefix + pinInfo.name + Environment.NewLine;

                        Marshal.ReleaseComObject(pins[0]);
                    }

                    Marshal.ReleaseComObject(pinEnum);
                }
                catch
                {
                    r = 0;
                    continue;
                }



            }

            Marshal.ReleaseComObject(enumFilters);


            var file2 = System.IO.File.CreateText(fileName);
            file2.AutoFlush = true;
            file2.Write(filterOutputString);
            file2.Close();
        }

        public string Splitter { get; set; }
        public string SplitterSource { get; set; }
        public string VideoDecoder { get; set; }
        public string AudioDecoder { get; set; }


        /// <summary>
        /// Opens the media by initializing the DirectShow graph
        /// </summary>
        protected virtual void OpenSource()
        {
            /* Make sure we clean up any remaining mess */
            FreeResources();

            if (m_sourceUri == null)
                return;

            string fileSource = m_sourceUri.OriginalString;

            if (string.IsNullOrEmpty(fileSource))
                return;

            try
            {
                /* Creates the GraphBuilder COM object */
                m_graph = new FilterGraphNoThread() as IGraphBuilder;

                if (m_graph == null)
                    throw new Exception("Could not create a graph");

                var filterGraph = m_graph as IFilterGraph2;

                if (filterGraph == null)
                    throw new Exception("Could not QueryInterface for the IFilterGraph2");

                IBaseFilter sourceFilter;
                int hr;

                //var file = System.IO.File.CreateText(@"M:\DirectShowLog.txt");
                //filterGraph.SetLogFile((file.BaseStream as System.IO.FileStream).SafeFileHandle.DangerousGetHandle());


                // Set LAV Splitter
                LAVSplitterSource reader = new LAVSplitterSource();
                sourceFilter = reader as IBaseFilter;
                var objectWithSite = reader as IObjectWithSite;
                if (objectWithSite != null)
                {
                    objectWithSite.SetSite(this);
                }

                hr = m_graph.AddFilter(sourceFilter, SplitterSource);
                DsError.ThrowExceptionForHR(hr);


                IFileSourceFilter interfaceFile = (IFileSourceFilter)sourceFilter;
                hr = interfaceFile.Load(fileSource, null);
                DsError.ThrowExceptionForHR(hr);


                // Set Video Codec
                // Remove Pin
                var videoPinFrom = DirectShowLib.DsFindPin.ByName(sourceFilter, "Video");
                IPin videoPinTo;
                if (videoPinFrom != null)
                {
                    hr = videoPinFrom.ConnectedTo(out videoPinTo);
                    if (hr >= 0 && videoPinTo != null)
                    {
                        PinInfo pInfo;
                        videoPinTo.QueryPinInfo(out pInfo);
                        FilterInfo fInfo;
                        pInfo.filter.QueryFilterInfo(out fInfo);

                        DirectShowUtil.DisconnectAllPins(m_graph, pInfo.filter);
                        m_graph.RemoveFilter(pInfo.filter);

                        DsUtils.FreePinInfo(pInfo);
                        Marshal.ReleaseComObject(fInfo.pGraph);
                        Marshal.ReleaseComObject(videoPinTo);
                        videoPinTo = null;
                    }
                    Marshal.ReleaseComObject(videoPinFrom);
                    videoPinFrom = null;
                }

                DirectShowUtil.AddFilterToGraph(m_graph, VideoDecoder, Guid.Empty);

                // Set Audio Codec
                // Remove Pin
                var audioPinFrom = DirectShowLib.DsFindPin.ByName(sourceFilter, "Audio");
                IPin audioPinTo;
                if (audioPinFrom != null)
                {
                    hr = audioPinFrom.ConnectedTo(out audioPinTo);
                    if (hr >= 0 && audioPinTo != null)
                    {
                        PinInfo pInfo;
                        audioPinTo.QueryPinInfo(out pInfo);
                        FilterInfo fInfo;
                        pInfo.filter.QueryFilterInfo(out fInfo);

                        DirectShowUtil.DisconnectAllPins(m_graph, pInfo.filter);
                        m_graph.RemoveFilter(pInfo.filter);

                        DsUtils.FreePinInfo(pInfo);
                        Marshal.ReleaseComObject(fInfo.pGraph);
                        Marshal.ReleaseComObject(audioPinTo);
                        audioPinTo = null;
                    }
                    Marshal.ReleaseComObject(audioPinFrom);
                    audioPinFrom = null;
                }

                DirectShowUtil.AddFilterToGraph(m_graph, AudioDecoder, Guid.Empty);


                /* Add our prefered audio renderer */
                InsertAudioRenderer(AudioRenderer);

                IBaseFilter renderer = CreateVideoRenderer(VideoRenderer, m_graph, 2);



                /* We will want to enum all the pins on the source filter */
                IEnumPins pinEnum;

                hr = sourceFilter.EnumPins(out pinEnum);
                DsError.ThrowExceptionForHR(hr);

                IntPtr fetched = IntPtr.Zero;
                IPin[] pins = { null };

                /* Counter for how many pins successfully rendered */
                int pinsRendered = 0;

                if (VideoRenderer == VideoRendererType.VideoMixingRenderer9)
                {
                    var mixer = renderer as IVMRMixerControl9;

                    if (mixer != null)
                    {
                        VMR9MixerPrefs dwPrefs;
                        mixer.GetMixingPrefs(out dwPrefs);
                        dwPrefs &= ~VMR9MixerPrefs.RenderTargetMask;
                        dwPrefs |= VMR9MixerPrefs.RenderTargetRGB;
                        //mixer.SetMixingPrefs(dwPrefs);
                    }
                }

                /* Test using FFDShow Video Decoder Filter
                var ffdshow = new FFDShow() as IBaseFilter;

                if (ffdshow != null)
                    m_graph.AddFilter(ffdshow, "ffdshow");
                */


                /* Loop over each pin of the source filter */
                while (pinEnum.Next(pins.Length, pins, fetched) == 0)
                {
                    if (filterGraph.RenderEx(pins[0],
                                             AMRenderExFlags.RenderToExistingRenderers,
                                             IntPtr.Zero) >= 0)
                        pinsRendered++;


                    Marshal.ReleaseComObject(pins[0]);
                }


                Marshal.ReleaseComObject(pinEnum);
                Marshal.ReleaseComObject(sourceFilter);

                if (pinsRendered == 0)
                    throw new Exception("Could not render any streams from the source Uri");

#if DEBUG
                /* Adds the GB to the ROT so we can view
                 * it in graphedit */
                m_dsRotEntry = new DsROTEntry(m_graph);
#endif
                /* Configure the graph in the base class */
                SetupFilterGraph(m_graph);

                HasVideo = true;
                /* Sets the NaturalVideoWidth/Height */
                //SetNativePixelSizes(renderer);
            }
            catch (Exception ex)
            {
                /* This exection will happen usually if the media does
                 * not exist or could not open due to not having the
                 * proper filters installed */
                FreeResources();

                /* Fire our failed event */
                InvokeMediaFailed(new MediaFailedEventArgs(ex.Message, ex));
            }

            InvokeMediaOpened();
        }

        /// <summary>
        /// Inserts the audio renderer by the name of
        /// the audio renderer that is passed
        /// </summary>
        protected virtual void InsertAudioRenderer(string audioDeviceName)
        {
            if (m_graph == null)
                return;

            AddFilterByName(m_graph, DirectShowLib.FilterCategory.AudioRendererCategory, audioDeviceName);
        }

        /// <summary>
        /// Frees all unmanaged memory and resets the object back
        /// to its initial state
        /// </summary>
        protected override void FreeResources()
        {
#if DEBUG
            /* Remove us from the ROT */
            if (m_dsRotEntry != null)
            {
                m_dsRotEntry.Dispose();
                m_dsRotEntry = null;
            }
#endif

            /* We run the StopInternal() to avoid any 
             * Dispatcher VeryifyAccess() issues because
             * this may be called from the GC */
            StopInternal();

            /* Let's clean up the base 
             * class's stuff first */
            base.FreeResources();

            if (m_graph != null)
            {
                Marshal.ReleaseComObject(m_graph);
                m_graph = null;

                /* Only run the media closed if we have an
                 * initialized filter graph */
                InvokeMediaClosed(new EventArgs());
            }
        }
    }
}