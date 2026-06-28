using System;

namespace SmartphoneAppMessenger
{
    public interface IStardewSocialApi
    {
        void CreateDraftPost(string? text = null, string? taggedNpc = null, string? imagePath = null, string? postTags = null);
        void CreateNpcPost(string authorName, string? taggedNpc = null, string? text = null, string? imagePath = null, string? postTags = null);
        void OpenProfile(string actorName);
    }
}
