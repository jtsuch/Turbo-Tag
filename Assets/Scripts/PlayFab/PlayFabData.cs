using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

public static class PlayFabData
{
    static Dictionary<string, UserDataRecord> userData;

    static bool isGettingUserData = false;
    public static void SaveData(Dictionary<string, string> Data,
        Action<UpdateUserDataResult> onSuccess,
        Action<PlayFabError> onFail
    )
    {
        PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest()
        {
            Data = Data
        },
        successResult =>
        {
            if (userData != null)
            {
                foreach (var entry in Data)
                {
                    if (userData.ContainsKey(entry.Key))
                        userData[entry.Key].Value = entry.Value;
                    else
                        userData.Add(entry.Key, new UserDataRecord() { Value = entry.Value });
                }
            }
            onSuccess(successResult);
        },
        onFail);
    }

    public static void GetUserData(
        Action<GetUserDataResult> onSuccess,
        Action<PlayFabError> onFail
    )
    {
        while(isGettingUserData)
        {
            // Wait until the current request is finished
            Task.Delay(100).Wait();
        }
        // If the data is already cached, skip
        if(userData != null)
        {
            onSuccess(new GetUserDataResult() { Data = userData });
            return;
        }
        isGettingUserData = true;
        PlayFabClientAPI.GetUserData(new GetUserDataRequest(),
        successResult =>
        {
            userData = successResult.Data;
            onSuccess(successResult);
        },
        onFail);
    }
}
