namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    // 生成数字页码包装列表
    private void UpdatePageNumbers()
    {
        InventoryPageNumbers.Clear();
        int total = InventoryTotalPages;
        int current = InventoryCurrentPage;

        void AddPage(int p)
        {
            InventoryPageNumbers.Add(new InventoryPageItem 
            { 
                PageNumber = p, 
                IsCurrent = (p == current) 
            });
        }

        if (total <= 7)
        {
            for (int i = 1; i <= total; i++)
                AddPage(i);
        }
        else
        {
            if (current <= 4)
            {
                for (int i = 1; i <= 5; i++)
                    AddPage(i);
                AddPage(0); // 省略号
                AddPage(total);
            }
            else if (current >= total - 3)
            {
                AddPage(1);
                AddPage(0); // 省略号
                for (int i = total - 4; i <= total; i++)
                    AddPage(i);
            }
            else
            {
                AddPage(1);
                AddPage(0); // 省略号
                AddPage(current - 1);
                AddPage(current);
                AddPage(current + 1);
                AddPage(0); // 省略号
                AddPage(total);
            }
        }
    }
}
