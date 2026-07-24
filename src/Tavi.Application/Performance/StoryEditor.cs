namespace Tavi.Application.Performance;

public class StoryEditor
{
    // TODO 地位等效于 WorldQueries。
    // 不同的是，不仅要提供读方法，还要提供写方法。
    // 写方法尤其重要：Manuscript 内部使用 string[] 管理正文，如果其中某个项变为空或者极小，可能需要组合；某一项被玩家改为极大，可能需要拆分。
}
