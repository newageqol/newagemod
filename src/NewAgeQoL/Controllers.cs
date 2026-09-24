using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class Controllers
    {
        private static IControllerContainer _holder;
        private static float _askAt;
        private static bool _listening;
        private static IUserData _user;

        internal static IUserData User
        {
            get
            {
                if (_user != null) return _user;
                try { _user = DependencyContainer.GetContainer()?.Resolve<IUserData>(); }
                catch (Exception) { _user = null; }
                return _user;
            }
        }

        internal static T Get<T>() where T : IController
        {
            var holder = Holder();
            return holder != null ? holder.GetController<T>() : default(T);
        }

        private static IControllerContainer Holder()
        {
            if (!_listening)
            {
                _listening = true;
                SceneManager.sceneLoaded += (scene, mode) => Forget();
            }
            if (_holder != null && (!(_holder is UnityEngine.Object alive) || alive != null)) return _holder;
            _holder = null;
            if (Time.unscaledTime < _askAt) return null;
            _askAt = Time.unscaledTime + 0.25f;
            try { _holder = DependencyContainer.GetContainer()?.Resolve<IControllerContainer>(); }
            catch (Exception) { _holder = null; }
            return _holder;
        }

        private static void Forget()
        {
            _holder = null;
            _askAt = 0f;
        }
    }
}
