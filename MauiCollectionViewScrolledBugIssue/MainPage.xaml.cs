using ApplePDF.PdfKit;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFiumCore;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls;

namespace MauiCollectionViewScrolledBugIssue;

public partial class MainPage : ContentPage
{
    /// <summary>
    /// 缩略图宽度
    /// </summary>
    public const int DesignThumbnailWidth = 300;

    PDFThumbnailViewViewModel viewModel;
    public MainPage()
    {
        InitializeComponent();
        collectionView.Scrolled += CollectionView_Scrolled;
        LoadMauiAsset();
    }

    async Task LoadMauiAsset()
    {
        var stream = await FileSystem.OpenAppPackageFileAsync("Vulkan.pdf");
        var doc = Pdfium.Instance.LoadPdfDocument(stream, string.Empty);
        PDFThumbnailViewViewModel.ItemSize = new Size(DesignThumbnailWidth, DesignThumbnailWidth / doc.GetPageSize(0).Width * doc.GetPageSize(0).Height);
        viewModel = new PDFThumbnailViewViewModel(doc);
        collectionView.BindingContext = viewModel;
        collectionView.ItemsSource = viewModel.PageImages;
    }

    int lastFirstVisibleItemIndex = -1;
    int lastLastVisibleItemIndex = -1;
    CancellationTokenSource tokenSource2;
    /// <summary>
    /// load more new image when scroll.
    /// remove old image that is disappeared.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void CollectionView_Scrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (lastFirstVisibleItemIndex == -1)
        {
            lastFirstVisibleItemIndex = e.FirstVisibleItemIndex;
            lastLastVisibleItemIndex = e.LastVisibleItemIndex;
            return;
        }
        if (tokenSource2 != null)
        {
            tokenSource2.Cancel();//取消上一个task
            tokenSource2.Dispose();
            tokenSource2 = null;
        }
        tokenSource2 = new CancellationTokenSource();
        CancellationToken ct = tokenSource2.Token;
        var task = Task.Run(() =>
        {
            var top = e.FirstVisibleItemIndex;
            var bottom = e.LastVisibleItemIndex;
            var latTop = lastFirstVisibleItemIndex;
            var latBottom = lastLastVisibleItemIndex;

            //如果中间的没有加载, 就加载
            for (int i = top; i <= bottom; i++)
            {
                if (tokenSource2.IsCancellationRequested)
                    return;
                if (viewModel.PageImages[i].Image == null)
                    viewModel.PageImages[i].LoadImage(viewModel.Document, PDFThumbnailViewViewModel.ItemSize);
            }

            bool topScroll = e.VerticalDelta > 0;

            int notRecycleOrAddMoreImageCountInDisappear = 0;
            if (topScroll)
            {
                //往上滑, 下面的数据是新加载的, 上面的是旧的

                //释放不见的,少释放十个
                for (var i = top - notRecycleOrAddMoreImageCountInDisappear; i > 0; i--)
                {
                    if (tokenSource2.IsCancellationRequested)
                        return;
                    if (viewModel.PageImages[i].Image == null)//遇到已经释放了的，那么之前的也已经释放
                        break;
                    viewModel.PageImages[i].RecycleImage();
                }

                //多加载10个
                for (var i = bottom; i < bottom + notRecycleOrAddMoreImageCountInDisappear; i++)
                {
                    if (tokenSource2.IsCancellationRequested)
                        return;
                    viewModel.PageImages[i].LoadImage(viewModel.Document, PDFThumbnailViewViewModel.ItemSize);
                }
            }
            else
            {
                //释放不见的,少释放十个
                for (var i = bottom + notRecycleOrAddMoreImageCountInDisappear; i < viewModel.PageImages.Count - 1; i++)
                {
                    if (tokenSource2.IsCancellationRequested)
                        return;
                    if (viewModel.PageImages[i].Image == null)
                        break;
                    viewModel.PageImages[i].RecycleImage();
                }

                //new appear, 加载新增可见的,多加载
                for (var i = top; i > top - notRecycleOrAddMoreImageCountInDisappear; i--)
                {
                    if (tokenSource2.IsCancellationRequested)
                        return;
                    viewModel.PageImages[i].LoadImage(viewModel.Document, PDFThumbnailViewViewModel.ItemSize);
                }
            }

        }, tokenSource2.Token);

        lastFirstVisibleItemIndex = e.FirstVisibleItemIndex;
        lastLastVisibleItemIndex = e.LastVisibleItemIndex;
    }

    public class PDFThumbnailViewViewModel
    {
        public static Size ItemSize;//大小需要根据界面大小来

        public PDFThumbnailViewViewModel(PdfDocument doc)
        {
            Document = doc;
            var pageCount = doc.PageCount;
            PageImages = new List<PageImageContainer>();
            for (int i = 0; i < pageCount; i++)
            {
                var image = new PageImageContainer() { PageIndex = i };
                PageImages.Add(image);
            }

            int initImageCount = (int)(DeviceDisplay.MainDisplayInfo.Height / (DesignThumbnailWidth * 1.2)) * 2;//pdf高度一般为高1.2倍
            Task.Run(() =>
            {
                for (int i = 0; i < pageCount; i++)
                {
                    if (i < initImageCount)
                    {
                        PageImages[i].LoadImage(doc, ItemSize);
                    }
                }
            });
        }

        public PdfDocument Document { get; set; }
        /// <summary>
        /// all page
        /// </summary>
        public List<PageImageContainer> PageImages { get; set; }
    }

    public partial class PageImageContainer : ObservableObject
    {
        [ObservableProperty]
        public int pageIndex;

        [ObservableProperty]
        public int height;

        [ObservableProperty]
        public int width;
        private MemoryStream stream;
        private SKBitmap bitmap;

        [ObservableProperty]
        public ImageSource image = null;

        public PageImageContainer()
        {
            Height = (int)PDFThumbnailViewViewModel.ItemSize.Height;
            Width = (int)PDFThumbnailViewViewModel.ItemSize.Width;
        }

        public void RecycleImage()
        {
            stream?.Close();
            stream = null;
            if (Image != null)
                (Image as SKBitmapImageSource).Bitmap = null;
            Image = null;
            bitmap?.Dispose();
            bitmap = null;
        }

        public void LoadImage(PdfDocument document, Size size)
        {
            if (bitmap == null)
            {
                var page = document.GetPage(PageIndex);
                var pageSize = page.GetSize();
                var flags = (int)(RenderFlags.None);
                var scale = size.Width / pageSize.Width;//pdfium绘制页面到图片时需要精度，展示时越大，需要越高的精度，因此这里使用当前需要展示的大小与pdf页面基本大小的比
                bitmap = PdfPageExtension.RenderPageToSKBitmapFormSKBitmap(page, (float)scale, flags, SKColors.White);
                if (Image == null)
                {
                    var source = new SKBitmapImageSource();
                    source.Bitmap = bitmap;
                    Image = source;
                }
            }
        }
    }
}

