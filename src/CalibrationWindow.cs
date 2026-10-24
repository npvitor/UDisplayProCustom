using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UDisplayPro
{
    public class CalibrationPattern : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w=ActualWidth;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(35,35,35)),null,new Rect(0,0,w,ActualHeight));
            DrawText(dc,"Padrão de comparação SDR — mova esta janela para o terceiro monitor",20,16,18,Brushes.White);
            DrawText(dc,"Desative contraste dinâmico/HDR para comparar. Neutro = brilho 0, contraste/saturação 100, gamma 1.",20,45,13,Brushes.White);
            int[][] rows = { new[] {0,4,8,12,16,20,24,28,32},new[] {223,227,231,235,239,243,247,251,255} };
            for(int row=0;row<2;row++)
            {
                double y=90+row*110;
                for(int i=0;i<rows[row].Length;i++)
                {
                    byte v=(byte)rows[row][i]; double cell=(w-40)/9;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(v,v,v)),null,new Rect(20+i*cell,y,cell,72));
                    DrawText(dc,v.ToString(),25+i*cell,y+78,13,Brushes.White);
                }
            }
            for(int i=0;i<256;i++)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)i,(byte)i,(byte)i)),null,new Rect(20+i*(w-40)/256,320,(w-40)/256+1,65));
            Color[] colors={Colors.Red,Colors.Lime,Colors.Blue,Colors.Cyan,Colors.Magenta,Colors.Yellow,Colors.Gray,Colors.White};
            for(int i=0;i<colors.Length;i++)
                dc.DrawRectangle(new SolidColorBrush(colors[i]),null,new Rect(20+i*(w-40)/8,410,(w-40)/8,65));
            DrawText(dc,"Procure gradações distintas perto do preto/branco, cinza sem tonalidade e cores sem recorte.",20,500,14,Brushes.White);
            DrawText(dc,"Texto colorido: ABC abc 012345  |  RGB / YCbCr  |  A compressão 4:2:0 pode borrar bordas.",20,530,15,Brushes.Cyan);
            DrawText(dc,"Comparação visual não substitui calibração com colorímetro/perfil ICC. Esc fecha; F11 tela cheia.",20,565,13,Brushes.White);
            for(int x=0;x<100;x++)
            {
                dc.DrawRectangle(x%2==0?Brushes.White:Brushes.Black,null,new Rect(20+x,605,1,45));
                dc.DrawRectangle(Brushes.White,null,new Rect(150+x,605+x/3,1,2));
            }
            DrawText(dc,"Traços finos e diagonal: compare Natural e Bordas Suaves.",280,615,15,Brushes.White);
        }
        void DrawText(DrawingContext dc,string value,double x,double y,double size,Brush brush)
        {
            dc.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
        }
    }
    public static class CalibrationWindow
    {
        public static void ShowPattern()
        {
            Window w=new Window {Title="UDisplay Pro — Padrão de Teste",Width=960,Height=740,MinWidth=900,MinHeight=720,Content=new CalibrationPattern()};
            w.KeyDown+=(s,e)=> {
                if(e.Key==System.Windows.Input.Key.Escape)w.Close();
                if(e.Key==System.Windows.Input.Key.F11) {
                    bool full=w.WindowStyle==WindowStyle.None;
                    w.WindowStyle=full?WindowStyle.SingleBorderWindow:WindowStyle.None;
                    w.WindowState=full?WindowState.Normal:WindowState.Maximized;
                }
            };
            w.Show();
        }
    }
}
